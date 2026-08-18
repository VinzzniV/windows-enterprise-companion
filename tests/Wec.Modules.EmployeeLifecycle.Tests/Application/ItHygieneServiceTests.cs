using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Modules.EmployeeLifecycle.Application;

namespace Wec.Modules.EmployeeLifecycle.Tests.Application;

public sealed class ItHygieneServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    private static readonly ItLifecycleOptions Options = new()
    {
        StaleWarningDays = 60,
        StaleCriticalDays = 90,
        TargetAgentVersion = "16.0.0.254",
        TargetKesVersion = "21.25.7.504",
    };

    [Fact]
    public void Correlation_NormalizesCaseAndFqdn()
    {
        IReadOnlyList<HygieneDevice> devices = Correlate(
            [Ad("pc001.example.test")],
            [Ksc("PC001")]);

        HygieneDevice device = Assert.Single(devices);
        Assert.Equal("PC001", device.ComputerName);
        Assert.True(device.ActiveDirectory.Exists);
        Assert.True(device.Kaspersky.Exists);
        Assert.Equal(HygieneStatus.Healthy, device.Assessment.Status);
    }

    [Fact]
    public void Assessment_RecognizesMissingAndOrphanWithoutFlaggingDisabledAdComputer()
    {
        IReadOnlyList<HygieneDevice> devices = Correlate(
            [Ad("MISSING"), Ad("DISABLED", enabled: false)],
            [Ksc("ORPHAN")]);

        AssertFinding(devices, "MISSING", HygieneFindingCode.MissingKaspersky);
        AssertFinding(devices, "ORPHAN", HygieneFindingCode.OrphanKaspersky);
        Assert.Empty(devices.Single(device => device.ComputerName == "DISABLED").Assessment.Findings);
    }

    [Fact]
    public void Assessment_EvaluatesAdAndKasperskyStalenessSeparately()
    {
        IReadOnlyList<HygieneDevice> devices = Correlate(
            [Ad("PC001", lastLogon: Now.AddDays(-61))],
            [Ksc("PC001", lastSeen: Now.AddDays(-91))]);

        HygieneDevice device = Assert.Single(devices);
        HygieneFinding ad = Assert.Single(
            device.Assessment.Findings,
            finding => finding.Code == HygieneFindingCode.StaleAd);
        HygieneFinding ksc = Assert.Single(
            device.Assessment.Findings,
            finding => finding.Code == HygieneFindingCode.StaleKaspersky);
        Assert.Equal(HygieneFindingSeverity.Warning, ad.Severity);
        Assert.Equal(HygieneFindingSeverity.Critical, ksc.Severity);
        Assert.Equal(HygieneStatus.CleanupCandidate, device.Assessment.Status);
    }

    [Fact]
    public void Assessment_RecognizesOutdatedAgentAndKes()
    {
        IReadOnlyList<HygieneDevice> devices = Correlate(
            [Ad("PC001")],
            [Ksc("PC001", agent: "15.1.0.1", kes: "21.24.9.1")]);

        HygieneDevice device = Assert.Single(devices);
        Assert.Contains(device.Assessment.Findings, finding => finding.Code == HygieneFindingCode.OutdatedAgent);
        Assert.Contains(device.Assessment.Findings, finding => finding.Code == HygieneFindingCode.OutdatedKes);
        Assert.Equal(HygieneStatus.Warning, device.Assessment.Status);
    }

    [Theory]
    [InlineData("16.0.0.253", "16.0.0.254", true)]
    [InlineData("16.0", "16.0.0.0", false)]
    [InlineData("16.1.0", "16.0.99", false)]
    [InlineData("unknown", "16.0.0.254", false)]
    [InlineData("16.0.0.254", "", false)]
    public void VersionComparison_IsSimpleAndPredictable(string installed, string target, bool expected)
    {
        Assert.Equal(expected, ItHygieneService.IsVersionOlder(installed, target));
    }

    [Theory]
    [InlineData(" pc001.EXAMPLE.test ", "PC001")]
    [InlineData("PC002.", "PC002")]
    [InlineData("", "")]
    public void ComputerNameNormalization_ReturnsShortUppercaseName(string input, string expected)
    {
        Assert.Equal(expected, ItHygieneService.NormalizeComputerName(input));
    }

    [Fact]
    public void Assessment_RecognizesMissingOrphanAndStaleOpsiForWindowsClients()
    {
        IReadOnlyList<HygieneDevice> devices = Correlate(
            [Ad("MISSING", operatingSystem: "Windows 11 Pro"), Ad("SERVER", operatingSystem: "Windows Server 2025")],
            [],
            [Opsi("ORPHAN"), Opsi("STALE", lastSeen: Now.AddDays(-91))]);

        AssertFinding(devices, "MISSING", HygieneFindingCode.MissingOpsi);
        AssertFinding(devices, "ORPHAN", HygieneFindingCode.OrphanOpsi);
        AssertFinding(devices, "STALE", HygieneFindingCode.StaleOpsi);
        Assert.DoesNotContain(
            devices.Single(device => device.ComputerName == "SERVER").Assessment.Findings,
            finding => finding.Code == HygieneFindingCode.MissingOpsi);
    }

    [Fact]
    public void Assessment_SuppressesAbsenceRulesWhenSourceIsUnavailable()
    {
        EnvironmentSourceStates sources = AvailableSources with
        {
            Opsi = new InventorySourceState(InventorySourceAvailability.NotConnected, "No session"),
        };

        IReadOnlyList<HygieneDevice> devices = ItHygieneService.CorrelateAndAssess(
            [Ad("PC001", operatingSystem: "Windows 11 Pro")],
            [Ksc("PC001")],
            [],
            sources,
            Now,
            Options);

        HygieneDevice device = Assert.Single(devices);
        Assert.DoesNotContain(device.Assessment.Findings, finding => finding.Code == HygieneFindingCode.MissingOpsi);
        Assert.Equal(HygieneStatus.Incomplete, device.Assessment.Status);
    }

    [Fact]
    public void Assessment_SuppressesMissingAndOrphanWhenEitherSourceIsTruncated()
    {
        EnvironmentSourceStates sources = AvailableSources with
        {
            Opsi = new InventorySourceState(InventorySourceAvailability.Truncated, "Limit reached"),
        };

        IReadOnlyList<HygieneDevice> devices = ItHygieneService.CorrelateAndAssess(
            [Ad("ADONLY", operatingSystem: "Windows 11 Pro")],
            [Ksc("ADONLY")],
            [Opsi("OPSIONLY")],
            sources,
            Now,
            Options);

        Assert.DoesNotContain(devices.SelectMany(device => device.Assessment.Findings),
            finding => finding.Code is HygieneFindingCode.MissingOpsi or HygieneFindingCode.OrphanOpsi);
        Assert.All(devices, device => Assert.Equal(HygieneStatus.Incomplete, device.Assessment.Status));
    }

    [Theory]
    [InlineData("Windows Server 2025")]
    [InlineData("Ubuntu 24.04 LTS")]
    [InlineData(null)]
    public void Assessment_DoesNotRequireOpsiForServersOrNonWindowsDevices(string? operatingSystem)
    {
        HygieneDevice device = Assert.Single(Correlate(
            [Ad("PC001", operatingSystem: operatingSystem)],
            [],
            []));

        Assert.DoesNotContain(device.Assessment.Findings,
            finding => finding.Code == HygieneFindingCode.MissingOpsi);
    }

    [Fact]
    public async Task LoadAsync_KeepsAvailableSourcesWhenKasperskyFails()
    {
        var service = new ItHygieneService(
            new AdProvider(Result.Success(new AdComputerInventory(
                true, "example.test", [Ad("PC001", operatingSystem: "Windows 11 Pro")], false))),
            new KasperskyProvider(Result.Failure<KasperskyInventory>(new Error(
                ErrorCode.ServiceUnavailable, "KSC unavailable"))),
            new OpsiProvider(Result.Success(new OpsiComputerInventory([Opsi("PC001")]))),
            new CredentialStore(),
            new TestClock(),
            Microsoft.Extensions.Options.Options.Create(Options));

        Result<ItHygieneResult> loaded = await service.LoadAsync(
            new ItHygieneRequest(Kaspersky: new KasperskyInventoryConnection(
                UserName: "reader", Password: "secret")),
            CancellationToken.None);

        Assert.True(loaded.IsSuccess);
        Assert.Equal(InventorySourceAvailability.Available, loaded.Value.Sources.ActiveDirectory.Availability);
        Assert.Equal(InventorySourceAvailability.Unavailable, loaded.Value.Sources.Kaspersky.Availability);
        Assert.Equal(InventorySourceAvailability.Available, loaded.Value.Sources.Opsi.Availability);
        HygieneDevice device = Assert.Single(loaded.Value.Devices);
        Assert.True(device.ActiveDirectory.Exists);
        Assert.True(device.Opsi.Exists);
        Assert.DoesNotContain(device.Assessment.Findings,
            finding => finding.Code == HygieneFindingCode.MissingKaspersky);
        Assert.Equal(HygieneStatus.Incomplete, device.Assessment.Status);
        Assert.Equal(1, loaded.Value.Summary.Incomplete);
        Assert.Equal(0, loaded.Value.Summary.Problems);
    }

    [Fact]
    public async Task LoadAsync_ContainsUnexpectedSourceException()
    {
        var service = new ItHygieneService(
            new AdProvider(Result.Success(new AdComputerInventory(
                true, "example.test", [Ad("PC001", operatingSystem: "Windows 11 Pro")], false))),
            new ThrowingKasperskyProvider(),
            new OpsiProvider(Result.Success(new OpsiComputerInventory([Opsi("PC001")]))),
            new CredentialStore(),
            new TestClock(),
            Microsoft.Extensions.Options.Options.Create(Options));

        Result<ItHygieneResult> loaded = await service.LoadAsync(
            new ItHygieneRequest(Kaspersky: new KasperskyInventoryConnection(
                UserName: "reader", Password: "secret")),
            CancellationToken.None);

        Assert.True(loaded.IsSuccess);
        Assert.Equal(InventorySourceAvailability.Unavailable, loaded.Value.Sources.Kaspersky.Availability);
        Assert.Contains("connection reset", loaded.Value.Sources.Kaspersky.Error);
        Assert.Single(loaded.Value.Devices);
    }

    [Fact]
    public async Task LoadAsync_UsesSavedKasperskyCredentialWhenRequestHasNone()
    {
        var kaspersky = new CapturingKasperskyProvider();
        var credentials = new CredentialStore(new StoredServiceCredential(
            "ksc-reader", "CORP", "stored-secret"));
        var service = new ItHygieneService(
            new AdProvider(Result.Success(new AdComputerInventory(
                true, "example.test", [Ad("PC001")], false))),
            kaspersky,
            new OpsiProvider(Result.Success(new OpsiComputerInventory([]))),
            credentials,
            new TestClock(),
            Microsoft.Extensions.Options.Options.Create(Options));

        Result<ItHygieneResult> loaded = await service.LoadAsync(
            new ItHygieneRequest(), CancellationToken.None);

        Assert.True(loaded.IsSuccess);
        Assert.NotNull(kaspersky.Connection);
        Assert.Equal("ksc-reader", kaspersky.Connection.UserName);
        Assert.Equal("CORP", kaspersky.Connection.Domain);
        Assert.Equal("stored-secret", kaspersky.Connection.Password);
    }

    private static readonly InventorySourceState Available =
        new(InventorySourceAvailability.Available);

    private static readonly EnvironmentSourceStates AvailableSources =
        new(Available, Available, Available);

    private static IReadOnlyList<HygieneDevice> Correlate(
        IReadOnlyList<AdComputerInventoryItem> ad,
        IReadOnlyList<KasperskyComputer> ksc,
        IReadOnlyList<OpsiComputerInventoryItem>? opsi = null) =>
        ItHygieneService.CorrelateAndAssess(
            ad,
            ksc,
            opsi ?? [],
            AvailableSources,
            Now,
            Options);

    private static AdComputerInventoryItem Ad(
        string name,
        bool enabled = true,
        DateTimeOffset? lastLogon = null,
        string? operatingSystem = null) =>
        new(
            name,
            $"{name}.example.test",
            operatingSystem,
            "Test client",
            enabled,
            $"CN={name},OU=Clients,DC=example,DC=test",
            lastLogon ?? Now.AddDays(-1));

    private static KasperskyComputer Ksc(
        string name,
        DateTimeOffset? lastSeen = null,
        string agent = "16.0.0.254",
        string kes = "21.25.7.504") =>
        new(name, lastSeen ?? Now.AddDays(-1), agent, kes, "Workstations");

    private static OpsiComputerInventoryItem Opsi(
        string name,
        DateTimeOffset? lastSeen = null) =>
        new($"{name}.example.test", "Test client", "depot.example.test", lastSeen ?? Now.AddDays(-1), "4.3.8.1");

    private static void AssertFinding(
        IEnumerable<HygieneDevice> devices,
        string computerName,
        HygieneFindingCode findingCode) =>
        Assert.Contains(
            devices.Single(device => device.ComputerName == computerName).Assessment.Findings,
            finding => finding.Code == findingCode);

    private sealed class AdProvider(Result<AdComputerInventory> result) : IAdComputerInventoryProvider
    {
        public Task<Result<AdComputerInventory>> LoadAsync(
            AdComputerInventoryQuery query,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class KasperskyProvider(Result<KasperskyInventory> result) : IKasperskyInventoryReader
    {
        public Task<Result<KasperskyInventory>> LoadAsync(
            KasperskyConnection connection,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class ThrowingKasperskyProvider : IKasperskyInventoryReader
    {
        public Task<Result<KasperskyInventory>> LoadAsync(
            KasperskyConnection connection,
            CancellationToken cancellationToken) => throw new IOException("connection reset");
    }

    private sealed class CapturingKasperskyProvider : IKasperskyInventoryReader
    {
        public KasperskyConnection? Connection { get; private set; }

        public Task<Result<KasperskyInventory>> LoadAsync(
            KasperskyConnection connection,
            CancellationToken cancellationToken)
        {
            Connection = connection;
            return Task.FromResult(Result.Success(new KasperskyInventory([], false)));
        }
    }

    private sealed class OpsiProvider(Result<OpsiComputerInventory> result) : IOpsiComputerInventoryProvider
    {
        public Task<Result<OpsiComputerInventory>> LoadAsync(
            int limit,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class CredentialStore(StoredServiceCredential? stored = null) : IServiceCredentialStore
    {
        public Result<StoredServiceCredential?> Read(ServiceCredentialKind kind) =>
            Result.Success<StoredServiceCredential?>(kind == ServiceCredentialKind.Kaspersky ? stored : null);
        public Result<bool> Save(ServiceCredentialKind kind, StoredServiceCredential credential) =>
            Result.Success(true);
        public Result<bool> Delete(ServiceCredentialKind kind) => Result.Success(true);
    }
}
