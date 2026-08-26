using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Modules.EmployeeLifecycle.Application;

namespace Wec.Modules.EmployeeLifecycle.Tests.Application;

public sealed class ClientOverviewServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
    private readonly IInventoryReportDataProvider _inventory = Substitute.For<IInventoryReportDataProvider>();
    private readonly IInstalledSoftwareInventoryProvider _software = Substitute.For<IInstalledSoftwareInventoryProvider>();
    private readonly IDeviceHealthSnapshotProvider _health = Substitute.For<IDeviceHealthSnapshotProvider>();
    private readonly ISecurityReportDataProvider _security = Substitute.For<ISecurityReportDataProvider>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public ClientOverviewServiceTests()
    {
        _clock.UtcNow.Returns(Now);
    }

    [Fact]
    public async Task StoredSources_MapToDenseOverviewWithoutRefreshingProviders()
    {
        DateTimeOffset capturedAt = Now.AddHours(-1);
        _inventory.GetLatestAsync("PC-42", Arg.Any<CancellationToken>()).Returns(Inventory(capturedAt));
        _software.GetLatestAsync("PC-42", Arg.Any<CancellationToken>()).Returns(Software(capturedAt));
        _health.GetLatestAsync("PC-42", Arg.Any<CancellationToken>()).Returns(Health(capturedAt));
        _security.GetLatestScanAsync("PC-42", Arg.Any<CancellationToken>()).Returns(Security(capturedAt));

        ClientOverviewResult result = await CreateService().GetAsync("PC-42", CancellationToken.None);

        Assert.Equal(17_179_869_184, result.Inventory!.TotalMemoryBytes);
        Assert.Equal("Windows 11 Enterprise", result.Inventory.OperatingSystem);
        Assert.Equal(12, result.Software!.InstalledCount);
        Assert.Equal(10, result.Software.Sample.Count);
        Assert.Equal("Application 01", result.Software.Sample[0].Name);
        Assert.Equal(1, result.Health!.WarningCount);
        Assert.Equal("Service stopped", Assert.Single(result.Health.Issues).Title);
        Assert.Equal(1, result.Security!.CriticalCount);
        Assert.All(result.Sources, source => Assert.Equal(ClientOverviewFreshness.Fresh, source.Freshness));
        Assert.All(result.Sources, source => Assert.True(source.IsComplete));
    }

    [Fact]
    public async Task MissingSources_RemainExplicitInsteadOfHealthy()
    {
        ClientOverviewResult result = await CreateService().GetAsync("PC-MISSING", CancellationToken.None);

        Assert.Null(result.Inventory);
        Assert.Null(result.Software);
        Assert.Null(result.Health);
        Assert.Null(result.Security);
        Assert.All(result.Sources, source =>
        {
            Assert.Equal(ClientOverviewFreshness.Missing, source.Freshness);
            Assert.False(source.IsComplete);
            Assert.Null(source.CapturedAtUtc);
        });
    }

    [Fact]
    public async Task LocalFqdn_UsesLocalProviderKeysAndFutureTimestampIsUnknown()
    {
        string localFqdn = $"{Environment.MachineName}.corp.example";
        _health.GetLatestAsync(host: null, Arg.Any<CancellationToken>())
            .Returns(Health(Now.AddMinutes(5)));

        ClientOverviewResult result = await CreateService().GetAsync(localFqdn, CancellationToken.None);

        Assert.Equal(ClientOverviewFreshness.Unknown, result.Health!.Metadata.Freshness);
        Assert.Null(result.Health.Metadata.AgeSeconds);
        await _inventory.Received(1).GetLatestAsync(host: null, Arg.Any<CancellationToken>());
        await _software.Received(1).GetLatestAsync(host: null, Arg.Any<CancellationToken>());
        await _health.Received(1).GetLatestAsync(host: null, Arg.Any<CancellationToken>());
        await _security.Received(1).GetLatestScanAsync(host: null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OldAndIncompleteSources_ReportBothStatesIndependently()
    {
        DateTimeOffset capturedAt = Now.AddDays(-2);
        _software.GetLatestAsync("PC-42", Arg.Any<CancellationToken>()).Returns(
            new InstalledSoftwareSnapshotData(
                "PC-42", capturedAt, IsComplete: false, [], "ACCESS_DENIED", "denied"));
        _health.GetLatestAsync("PC-42", Arg.Any<CancellationToken>()).Returns(
            new DeviceHealthSnapshotData(capturedAt, IsComplete: false, 4, 2, []));

        ClientOverviewResult result = await CreateService().GetAsync("PC-42", CancellationToken.None);

        Assert.Equal(ClientOverviewFreshness.Stale, result.Software!.Metadata.Freshness);
        Assert.False(result.Software.Metadata.IsComplete);
        Assert.Contains("ACCESS_DENIED", result.Software.Metadata.Coverage, StringComparison.Ordinal);
        Assert.Equal(ClientOverviewFreshness.Stale, result.Health!.Metadata.Freshness);
        Assert.False(result.Health.Metadata.IsComplete);
    }

    private ClientOverviewService CreateService() => new(
        _inventory,
        _software,
        _health,
        _security,
        _clock,
        Options.Create(new ClientOverviewOptions()));

    private static InventoryReportData Inventory(DateTimeOffset capturedAt) => new(
        capturedAt,
        new CpuReportData("Intel Core", 8, 16, 4200),
        [
            new MemoryBankReportData("Memory", "A", 8_589_934_592, 3200),
            new MemoryBankReportData("Memory", "B", 8_589_934_592, 3200),
        ],
        [new DiskReportData("NVMe", 512_000_000_000, "NVMe")],
        new OperatingSystemReportData("Windows 11 Enterprise", "10.0", "26100", "64-bit"));

    private static InstalledSoftwareSnapshotData Software(DateTimeOffset capturedAt) => new(
        "PC-42",
        capturedAt,
        IsComplete: true,
        [.. Enumerable.Range(1, 12).Select(index =>
            new InstalledSoftwareRecordData($"Application {index:D2}", $"{index}.0", "Vendor"))],
        ErrorCode: null,
        ErrorMessage: null);

    private static DeviceHealthSnapshotData Health(DateTimeOffset capturedAt) => new(
        capturedAt,
        IsComplete: true,
        ExpectedCheckCount: 4,
        ObservedCheckCount: 4,
        [
            new DeviceHealthCheckData("UPDATE", "Updates current", "Pass", "System", "Windows Update", capturedAt),
            new DeviceHealthCheckData("SERVICES", "Service stopped", "Warning", "Services", "Spooler", capturedAt),
            new DeviceHealthCheckData("EVENTS", "Event log quiet", "Pass", "EventLog", "System", capturedAt),
            new DeviceHealthCheckData("DISK", "Disk healthy", "Pass", "System", "C:", capturedAt),
        ]);

    private static SecurityReportData Security(DateTimeOffset capturedAt) => new(
        capturedAt,
        "Completed",
        [
            new SecurityFindingReportData(
                "CRITICAL-1", "Critical finding", "Description", "Critical", 4,
                "System", "PC-42", "Fix it", null),
            new SecurityFindingReportData(
                "LOW-1", "Low finding", "Description", "Low", 1,
                "System", "PC-42", "Review it", null),
        ],
        new SecurityCoverageReportData(true, true, 10, 8, 8, 0, 0, 2),
        []);
}
