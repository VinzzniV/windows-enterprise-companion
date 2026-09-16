using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Messaging;
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
    public void CorrelationRetainsFqdnAndLabelsNameComparisonAsCandidate()
    {
        IReadOnlyList<HygieneDevice> devices = Correlate(
            [Ad("pc001.example.test")],
            [Ksc("PC001")]);

        HygieneDevice device = Assert.Single(devices);
        Assert.Equal("pc001.example.test", device.ComputerName);
        Assert.Equal(Wec.Core.Objects.IdentityEvidence.AliasCandidate, device.Correlation);
        Assert.NotNull(device.EvidenceKey);
        Assert.True(device.ActiveDirectory.Exists);
        Assert.True(device.Kaspersky.Exists);
        Assert.Equal(HygieneStatus.Healthy, device.Assessment.Status);
    }

    [Fact]
    public void DuplicateRecordsAndNamespacesArePreservedWithoutLatestSelectionOrAbsenceClaims()
    {
        var first = Ad("PC") with { ObjectId = Guid.NewGuid() };
        var rows = Correlate([first, first with { ObjectId = Guid.NewGuid(), DnsHostName = "pc.other.test" }],
            [Ksc("PC", agent: "old"), Ksc("PC", lastSeen: Now, agent: "new")], [Opsi("PC")]);
        Assert.Equal(5, rows.Count);
        Assert.Equal(5, rows.Select(row => row.EvidenceKey).Distinct().Count());
        Assert.All(rows, row =>
        {
            Assert.False(row.CanTargetWindows);
            Assert.Equal(Wec.Core.Objects.IdentityEvidence.Ambiguous, row.Correlation);
            Assert.DoesNotContain(row.Assessment.Findings, finding => finding.Code is HygieneFindingCode.MissingKaspersky
                or HygieneFindingCode.OrphanKaspersky or HygieneFindingCode.MissingOpsi or HygieneFindingCode.OrphanOpsi);
        });
        Assert.Equal(new[] { "new", "old" }, rows.Where(row => row.Kaspersky.Exists).Select(row => row.Kaspersky.AgentVersion).Order().ToArray());
        Assert.Contains(rows, row => row.HostName == "pc.other.test");
    }

    [Fact]
    public void NessusOnlyIpAddressesAndUnknownNamesRemainSeparateEvidence()
    {
        var first = new NessusComputerInventoryItem("192.0.2.10", null, "192.0.2.10", Now, 1, 0, 0, 0, 0, [], []);
        var inventory = new NessusComputerInventory([first, first with { ComputerName = "192.0.2.11", IpAddress = "192.0.2.11" },
            first with { ComputerName = "", IpAddress = null }, first with { ComputerName = "", IpAddress = null }], NessusInventoryAvailability.Available, Now);
        var rows = ItHygieneService.CorrelateAndAssess([], [], [], inventory,
            new(Available, Available, Available, Available), Now, Options);
        Assert.Equal(4, rows.Count);
        Assert.Equal(4, rows.Select(row => row.EvidenceKey).Distinct().Count());
        Assert.All(rows, row => { Assert.False(row.CanTargetWindows); Assert.True(row.Nessus.Exists); });
        Assert.Contains(rows, row => row.HostName == "192.0.2.10");
        Assert.Contains(rows, row => row.HostName == "192.0.2.11");
        Assert.Equal("192.0.2.10", ItHygieneService.NormalizeComputerName("192.0.2.10"));
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

    [Fact]
    public void Assessment_DetectsRegisteredKasperskyHostWithoutAgentOrKes()
    {
        HygieneDevice device = Assert.Single(Correlate(
            [Ad("PC001")],
            [Ksc("PC001", agent: "", kes: "")]));

        AssertFinding([device], "PC001", HygieneFindingCode.MissingKasperskyAgent);
        AssertFinding([device], "PC001", HygieneFindingCode.MissingKes);
        Assert.Equal(HygieneStatus.Warning, device.Assessment.Status);
    }

    [Theory]
    [InlineData("16.0.0.253", "16.0.0.254", true)]
    [InlineData("16.0", "16.0.0.0", false)]
    [InlineData("16.1.0", "16.0.99", false)]
    [InlineData("16.0.0-beta", "16.0.1", false)]
    [InlineData("16.0.", "16.0.1", false)]
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
    public void NessusAssessment_RequiresEnabledWindowsServersAndClients()
    {
        var nessus = new NessusComputerInventory([], NessusInventoryAvailability.Available, Now);
        var sources = new EnvironmentSourceStates(Available, Available, Available, Available);
        IReadOnlyList<HygieneDevice> devices = ItHygieneService.CorrelateAndAssess(
            [Ad("CLIENT", operatingSystem: "Windows 11 Pro"), Ad("SERVER", operatingSystem: "Windows Server 2025"), Ad("LINUX", operatingSystem: "Ubuntu")],
            [], [], nessus, sources, Now, Options);

        AssertFinding(devices, "CLIENT", HygieneFindingCode.MissingNessus);
        AssertFinding(devices, "SERVER", HygieneFindingCode.MissingNessus);
        Assert.DoesNotContain(devices.Single(x => x.ComputerName == "LINUX").Assessment.Findings,
            x => x.Code == HygieneFindingCode.MissingNessus);
    }

    [Fact]
    public void NessusAssessment_SuppressesMissingForPartialDataAndConfiguredExclusions()
    {
        var partial = new NessusComputerInventory([], NessusInventoryAvailability.Partial, Now, "one scan failed");
        var partialSources = new EnvironmentSourceStates(Available, Available, Available,
            new InventorySourceState(InventorySourceAvailability.Partial));
        HygieneDevice incomplete = Assert.Single(ItHygieneService.CorrelateAndAssess(
            [Ad("CLIENT", operatingSystem: "Windows 11")], [Ksc("CLIENT")], [Opsi("CLIENT")], partial, partialSources, Now, Options));
        Assert.Equal(HygieneStatus.Incomplete, incomplete.Assessment.Status);
        Assert.DoesNotContain(incomplete.Assessment.Findings, x => x.Code == HygieneFindingCode.MissingNessus);

        var excluded = new NessusComputerInventory([], NessusInventoryAvailability.Available, Now,
            MissingExcludedHostPatterns: ["CLIENT"]);
        HygieneDevice ignored = Assert.Single(ItHygieneService.CorrelateAndAssess(
            [Ad("CLIENT", operatingSystem: "Windows 11")], [Ksc("CLIENT")], [Opsi("CLIENT")], excluded,
            new EnvironmentSourceStates(Available, Available, Available, Available), Now, Options));
        Assert.DoesNotContain(ignored.Assessment.Findings, x => x.Code == HygieneFindingCode.MissingNessus);
    }

    [Fact]
    public void NessusAssessment_UsesCriticalStatusWithoutCleanupCandidate()
    {
        var inventory = new NessusComputerInventory(
            [new NessusComputerInventoryItem("CLIENT", "asset", "10.0.0.1", Now.AddDays(-1), 2, 4, 1, 0, 0, [443], ["Clients"])],
            NessusInventoryAvailability.Available, Now);
        HygieneDevice device = Assert.Single(ItHygieneService.CorrelateAndAssess(
            [Ad("CLIENT", operatingSystem: "Windows 11")], [], [], inventory,
            new EnvironmentSourceStates(Available, Available, Available, Available), Now, Options));
        Assert.Equal(HygieneStatus.Critical, device.Assessment.Status);
        AssertFinding([device], "CLIENT", HygieneFindingCode.NessusCriticalVulnerabilities);
    }

    [Fact]
    public void NessusAssessment_TreatsAssetWithoutCompletedScanAsMissing()
    {
        var inventory = new NessusComputerInventory(
            [new NessusComputerInventoryItem("CLIENT", "asset", "10.0.0.1", null, 2, 0, 0, 0, 0, [], ["Clients"])],
            NessusInventoryAvailability.Available, Now);
        HygieneDevice device = Assert.Single(ItHygieneService.CorrelateAndAssess(
            [Ad("CLIENT", operatingSystem: "Windows 11")], [Ksc("CLIENT")], [Opsi("CLIENT")], inventory,
            new EnvironmentSourceStates(Available, Available, Available, Available), Now, Options));

        AssertFinding([device], "CLIENT", HygieneFindingCode.MissingNessus);
        Assert.DoesNotContain(device.Assessment.Findings,
            finding => finding.Code == HygieneFindingCode.NessusCriticalVulnerabilities);
        Assert.Equal(HygieneStatus.Warning, device.Assessment.Status);
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
            new NessusProvider(),
            new CredentialStore(),
            new EventPublisher(),
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
            new NessusProvider(),
            new CredentialStore(),
            new EventPublisher(),
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
            new NessusProvider(),
            credentials,
            new EventPublisher(),
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

    [Fact]
    public async Task LoadAsync_DoesNotUseActiveDirectoryCredentialForKaspersky()
    {
        var kaspersky = new CapturingKasperskyProvider();
        var service = new ItHygieneService(
            new AdProvider(Result.Success(new AdComputerInventory(true, "example.test", [Ad("PC001")], false))),
            kaspersky,
            new OpsiProvider(Result.Success(new OpsiComputerInventory([]))),
            new NessusProvider(),
            new CredentialStore(),
            new EventPublisher(),
            new TestClock(),
            Microsoft.Extensions.Options.Options.Create(Options));

        Result<ItHygieneResult> loaded = await service.LoadAsync(new ItHygieneRequest(
            ActiveDirectory: new DirectoryInventoryConnection(
                UserName: "administrator", UserDomain: "CORP", Password: "admin-secret")),
            CancellationToken.None);

        Assert.True(loaded.IsSuccess);
        Assert.Null(kaspersky.Connection);
        Assert.Equal(InventorySourceAvailability.NotConnected, loaded.Value.Sources.Kaspersky.Availability);
    }

    [Fact]
    public async Task LoadAsync_PublishesCorrelatedSourceProgressAndPartialSummaries()
    {
        var events = new EventPublisher();
        var service = new ItHygieneService(
            new AdProvider(Result.Success(new AdComputerInventory(
                true, "example.test", [Ad("PC001")], false))),
            new KasperskyProvider(Result.Success(new KasperskyInventory([Ksc("PC001")], false))),
            new OpsiProvider(Result.Success(new OpsiComputerInventory([Opsi("PC001")]))),
            new NessusProvider(),
            new CredentialStore(),
            events,
            new TestClock(),
            Microsoft.Extensions.Options.Options.Create(Options));

        Result<ItHygieneResult> loaded = await service.LoadAsync(
            new ItHygieneRequest(
                Kaspersky: new KasperskyInventoryConnection(UserName: "reader", Password: "secret"),
                OperationId: "operation-1"),
            CancellationToken.None);

        Assert.True(loaded.IsSuccess);
        HygieneLoadProgress[] progress = events.Progress.ToArray();
        Assert.NotEmpty(progress);
        Assert.All(progress, item => Assert.Equal("operation-1", item.OperationId));
        Assert.Equal(HygieneLoadPhase.LoadingSources, progress[0].Phase);
        Assert.Equal(0, progress[0].CompletedSources);
        Assert.Contains(progress, item => item.CompletedSources is > 0 and < 4 && item.PartialDeviceCount > 0);
        Assert.Contains(progress, item => item.Phase == HygieneLoadPhase.Correlating && item.CompletedSources == 4);
        HygieneLoadProgress completed = Assert.Single(progress, item => item.Phase == HygieneLoadPhase.Completed);
        Assert.Equal(4, completed.CompletedSources);
        Assert.Equal(loaded.Value.Summary, completed.PartialSummary);
        Assert.All(completed.Sources, source => Assert.NotEqual(HygieneSourceProgressStatus.Running, source.Status));
    }

    [Fact]
    public async Task LoadAsync_PublishesCancelledAndPropagatesCancellationToAProvider()
    {
        var events = new EventPublisher();
        var blocking = new BlockingKasperskyProvider();
        var service = new ItHygieneService(
            new AdProvider(Result.Success(new AdComputerInventory(true, "example.test", [], false))),
            blocking,
            new OpsiProvider(Result.Success(new OpsiComputerInventory([]))),
            new NessusProvider(),
            new CredentialStore(),
            events,
            new TestClock(),
            Microsoft.Extensions.Options.Options.Create(Options));
        using var cancellation = new CancellationTokenSource();

        Task<Result<ItHygieneResult>> load = service.LoadAsync(
            new ItHygieneRequest(
                Kaspersky: new KasperskyInventoryConnection(UserName: "reader", Password: "secret"),
                OperationId: "cancel-me"),
            cancellation.Token);
        await blocking.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);
        Assert.True(blocking.ObservedCancellation);
        Assert.Contains(events.Progress, item => item.OperationId == "cancel-me" && item.Phase == HygieneLoadPhase.Cancelled);
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
            name.Contains('.') ? name : $"{name}.example.test",
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
            devices.Single(device => ItHygieneService.NormalizeComputerName(device.ComputerName) == computerName).Assessment.Findings,
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

    private sealed class BlockingKasperskyProvider : IKasperskyInventoryReader
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ObservedCancellation { get; private set; }

        public async Task<Result<KasperskyInventory>> LoadAsync(
            KasperskyConnection connection,
            CancellationToken cancellationToken)
        {
            Started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Result.Success(new KasperskyInventory([], false));
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation = true;
                throw;
            }
        }
    }

    private sealed class OpsiProvider(Result<OpsiComputerInventory> result) : IOpsiComputerInventoryProvider
    {
        public Guid? CurrentSessionId => null;
        public Task<Result<OpsiComputerInventory>> LoadAsync(
            int limit,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class NessusProvider : INessusComputerInventoryProvider
    {
        public Task<Result<NessusComputerInventory>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new NessusComputerInventory(
                [new NessusComputerInventoryItem("PC001", "asset-1", "10.0.0.1", Now.AddDays(-1), 0, 0, 0, 0, 0, [], ["Clients"])],
                NessusInventoryAvailability.Available,
                Now)));
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class EventPublisher : IBridgeEventPublisher
    {
        private readonly List<HygieneLoadProgress> _progress = [];
        public IReadOnlyList<HygieneLoadProgress> Progress
        {
            get
            {
                lock (_progress)
                {
                    return _progress.ToArray();
                }
            }
        }

        public void Publish(BridgeEvent bridgeEvent)
        {
            if (bridgeEvent.Payload is not HygieneLoadProgress progress)
            {
                return;
            }

            lock (_progress)
            {
                _progress.Add(progress);
            }
        }
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
