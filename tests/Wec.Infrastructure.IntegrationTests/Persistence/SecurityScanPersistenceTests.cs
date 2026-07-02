using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wec.Core.Privileges;
using Wec.Infrastructure.Persistence;
using Wec.Modules.Security.Domain;
using Wec.Modules.Security.Persistence;

namespace Wec.Infrastructure.IntegrationTests.Persistence;

public sealed class SecurityScanPersistenceTests : IDisposable
{
    private static readonly DateTimeOffset StartedAt = new(2026, 7, 2, 15, 0, 0, TimeSpan.Zero);

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"wec-integration-{Guid.NewGuid():N}.db");

    private WecDbContext CreateContext() => IntegrationDbContextFactory.Create(_databasePath);

    private static SecurityFinding BuildFinding() => new(
        "WEC-SEC-FIREWALL-DISABLED",
        "Windows Firewall is disabled for the Public profile",
        "The profile is turned off.",
        FindingSeverity.High,
        FindingCategory.Firewall,
        "Firewall profile 'Public'",
        new Dictionary<string, string> { ["profile"] = "Public", ["enabled"] = "false" },
        "Enable the firewall.",
        PrivilegeLevel.Administrator,
        StartedAt);

    [Fact]
    public async Task ScanWithFindings_SurvivesRoundTripThroughRealMigrationAndNewContext()
    {
        long scanId;
        using (WecDbContext writeContext = CreateContext())
        {
            await writeContext.Database.MigrateAsync();
            var repository = new EfSecurityScanRepository(
                writeContext, NullLogger<EfSecurityScanRepository>.Instance);
            scanId = await repository.SaveScanAsync(
                StartedAt,
                StartedAt.AddSeconds(3),
                ScanStatus.Completed,
                [BuildFinding()],
                CancellationToken.None);
        }

        using WecDbContext readContext = CreateContext();
        var reloadedRepository = new EfSecurityScanRepository(
            readContext, NullLogger<EfSecurityScanRepository>.Instance);
        SecurityScanResult? reloaded = await reloadedRepository.GetLatestScanAsync(CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal(scanId, reloaded.ScanId);
        Assert.Equal(StartedAt, reloaded.StartedAtUtc);
        Assert.Equal(ScanStatus.Completed, reloaded.Status);
        SecurityFinding finding = Assert.Single(reloaded.Findings);
        Assert.Equal(FindingSeverity.High, finding.Severity);
        Assert.Equal(FindingCategory.Firewall, finding.Category);
        Assert.Equal(PrivilegeLevel.Administrator, finding.RequiredPrivilege);
        Assert.Equal("Public", finding.Evidence["profile"]);
    }

    [Fact]
    public async Task GetLatestScan_ReturnsMostRecentScanWithHistoryPreserved()
    {
        using WecDbContext context = CreateContext();
        await context.Database.MigrateAsync();
        var repository = new EfSecurityScanRepository(
            context, NullLogger<EfSecurityScanRepository>.Instance);

        await repository.SaveScanAsync(
            StartedAt.AddHours(-1), StartedAt.AddHours(-1), ScanStatus.Completed, [], CancellationToken.None);
        long latestScanId = await repository.SaveScanAsync(
            StartedAt, StartedAt.AddSeconds(2), ScanStatus.CompletedWithErrors, [BuildFinding()], CancellationToken.None);

        SecurityScanResult? latest = await repository.GetLatestScanAsync(CancellationToken.None);
        int totalScans = await context.Set<SecurityScanRecord>().CountAsync();

        Assert.NotNull(latest);
        Assert.Equal(latestScanId, latest.ScanId);
        Assert.Equal(ScanStatus.CompletedWithErrors, latest.Status);
        Assert.Equal(2, totalScans);
    }

    [Fact]
    public async Task GetRecentScans_ReturnsNewestFirstAndHonorsTheLimit()
    {
        using WecDbContext context = CreateContext();
        await context.Database.MigrateAsync();
        var repository = new EfSecurityScanRepository(
            context, NullLogger<EfSecurityScanRepository>.Instance);

        for (int scanNumber = 0; scanNumber < 3; scanNumber++)
        {
            await repository.SaveScanAsync(
                StartedAt.AddHours(scanNumber),
                StartedAt.AddHours(scanNumber).AddSeconds(2),
                ScanStatus.Completed,
                scanNumber == 2 ? [BuildFinding()] : [],
                CancellationToken.None);
        }

        IReadOnlyList<SecurityScanResult> recent =
            await repository.GetRecentScansAsync(2, CancellationToken.None);

        Assert.Equal(2, recent.Count);
        Assert.True(recent[0].ScanId > recent[1].ScanId);
        Assert.Single(recent[0].Findings);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
