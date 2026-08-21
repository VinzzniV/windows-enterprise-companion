using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Wec.Core.Privileges;
using Wec.Core.Results;
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
                "PC-001",
                StartedAt,
                StartedAt.AddSeconds(3),
                ScanStatus.Completed,
                [BuildFinding()],
                CancellationToken.None);
        }

        using WecDbContext readContext = CreateContext();
        var reloadedRepository = new EfSecurityScanRepository(
            readContext, NullLogger<EfSecurityScanRepository>.Instance);
        SecurityScanResult? reloaded = await reloadedRepository.GetLatestScanAsync("PC-001", CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal(scanId, reloaded.ScanId);
        Assert.Equal(StartedAt, reloaded.StartedAtUtc);
        Assert.Equal(ScanStatus.Completed, reloaded.Status);
        SecurityFinding finding = Assert.Single(reloaded.Findings);
        Assert.Equal(FindingSeverity.High, finding.Severity);
        Assert.Equal(FindingCategory.Firewall, finding.Category);
        Assert.Equal(PrivilegeLevel.Administrator, finding.RequiredPrivilege);
        Assert.Equal("Public", finding.Evidence["profile"]);
        Assert.False(reloaded.Coverage.IsKnown);
    }

    [Fact]
    public async Task GetLatestScan_ReturnsMostRecentScanWithHistoryPreserved()
    {
        using WecDbContext context = CreateContext();
        await context.Database.MigrateAsync();
        var repository = new EfSecurityScanRepository(
            context, NullLogger<EfSecurityScanRepository>.Instance);

        await repository.SaveScanAsync(
            "PC-001",
            StartedAt.AddHours(-1), StartedAt.AddHours(-1), ScanStatus.Completed, [], CancellationToken.None);
        long latestScanId = await repository.SaveScanAsync(
            "PC-001",
            StartedAt, StartedAt.AddSeconds(2), ScanStatus.CompletedWithErrors, [BuildFinding()], CancellationToken.None);

        SecurityScanResult? latest = await repository.GetLatestScanAsync("PC-001", CancellationToken.None);
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
                "PC-001",
                StartedAt.AddHours(scanNumber),
                StartedAt.AddHours(scanNumber).AddSeconds(2),
                ScanStatus.Completed,
                scanNumber == 2 ? [BuildFinding()] : [],
                CancellationToken.None);
        }

        IReadOnlyList<SecurityScanResult> recent =
            await repository.GetRecentScansAsync("PC-001", 2, CancellationToken.None);

        Assert.Equal(2, recent.Count);
        Assert.True(recent[0].ScanId > recent[1].ScanId);
        Assert.Single(recent[0].Findings);
    }

    [Fact]
    public async Task ListHosts_ReturnsOnlyTheNewestScanStampForEachHost()
    {
        using WecDbContext context = CreateContext();
        await context.Database.MigrateAsync();
        var repository = new EfSecurityScanRepository(
            context, NullLogger<EfSecurityScanRepository>.Instance);

        await repository.SaveScanAsync(
            "PC-B",
            StartedAt.AddHours(-2), StartedAt.AddHours(-2).AddSeconds(2),
            ScanStatus.Completed, [], CancellationToken.None);
        await repository.SaveScanAsync(
            "PC-A",
            StartedAt.AddHours(-1), StartedAt.AddHours(-1).AddSeconds(2),
            ScanStatus.Completed, [], CancellationToken.None);
        await repository.SaveScanAsync(
            "PC-A",
            StartedAt, StartedAt.AddSeconds(3),
            ScanStatus.CompletedWithErrors, [BuildFinding()], CancellationToken.None);

        IReadOnlyList<StoredSecurityScanHost> hosts =
            await repository.ListHostsAsync(CancellationToken.None);

        Assert.Collection(
            hosts,
            host =>
            {
                Assert.Equal("PC-A", host.Host);
                Assert.Equal(StartedAt.AddSeconds(3), host.CompletedAtUtc);
            },
            host =>
            {
                Assert.Equal("PC-B", host.Host);
                Assert.Equal(StartedAt.AddHours(-2).AddSeconds(2), host.CompletedAtUtc);
            });
    }

    [Fact]
    public async Task CheckCoverage_SurvivesRoundTripThroughRealMigrationAndNewContext()
    {
        long scanId;
        using (WecDbContext writeContext = CreateContext())
        {
            await writeContext.Database.MigrateAsync();
            var repository = new EfSecurityScanRepository(
                writeContext, NullLogger<EfSecurityScanRepository>.Instance);
            scanId = await repository.SaveScanAsync(
                "PC-001",
                StartedAt,
                StartedAt.AddSeconds(3),
                ScanStatus.CompletedWithErrors,
                SecurityCoverage.CurrentVersion,
                [
                    new SecurityCheckResult("FIREWALL", CheckStatus.Succeeded, [BuildFinding()]),
                    new SecurityCheckResult(
                        "TPM",
                        CheckStatus.RequiresElevation,
                        [],
                        new SecurityCheckFailure(
                            ErrorCode.AccessDenied,
                            "Administrator privileges are required.",
                            PrivilegeLevel.Administrator)),
                    new SecurityCheckResult(
                        "WMI",
                        CheckStatus.Failed,
                        [],
                        new SecurityCheckFailure(
                            ErrorCode.WmiUnavailable,
                            "The provider could not be queried.",
                            RequiredPrivilege: null)),
                    new SecurityCheckResult("LOCAL-ONLY", CheckStatus.NotApplicable, []),
                ],
                CancellationToken.None);
        }

        using WecDbContext readContext = CreateContext();
        var reloadedRepository = new EfSecurityScanRepository(
            readContext, NullLogger<EfSecurityScanRepository>.Instance);
        SecurityScanResult? reloaded = await reloadedRepository.GetLatestScanAsync(
            "PC-001", CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal(scanId, reloaded.ScanId);
        Assert.Equal(SecurityCoverage.CurrentVersion, reloaded.CoverageVersion);
        Assert.Equal(4, reloaded.Coverage.TotalChecks);
        Assert.Equal(3, reloaded.Coverage.ApplicableChecks);
        Assert.Equal(1, reloaded.Coverage.SucceededChecks);
        Assert.Equal(1, reloaded.Coverage.FailedChecks);
        Assert.Equal(1, reloaded.Coverage.RequiresElevationChecks);
        Assert.Equal(1, reloaded.Coverage.NotApplicableChecks);
        Assert.False(reloaded.Coverage.IsComplete);
        Assert.Equal(BuildFinding().FindingId, Assert.Single(reloaded.Findings).FindingId);
        SecurityCheckResult tpm = Assert.Single(reloaded.CheckResults, result => result.CheckId == "TPM");
        Assert.Equal(ErrorCode.AccessDenied, tpm.Failure?.Code);
        Assert.Equal(PrivilegeLevel.Administrator, tpm.Failure?.RequiredPrivilege);
        Assert.Empty(tpm.Findings);
        Assert.Single(Assert.Single(reloaded.CheckResults, result => result.CheckId == "FIREWALL").Findings);
    }

    [Fact]
    public async Task ExistingDatabase_MigratesLegacyScansAsCoverageUnknownWithoutInferringOutcomes()
    {
        using (WecDbContext oldContext = CreateContext())
        {
            IMigrator migrator = oldContext.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20260818134013_AddNessusVulnerabilityManagement");
            await oldContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO security_scans
                    (host, started_at_utc, completed_at_utc, status, finding_count)
                VALUES
                    ({"PC-LEGACY"}, {StartedAt.UtcTicks}, {StartedAt.AddSeconds(2).UtcTicks}, {"Completed"}, {1})
                """);
            await oldContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO security_findings
                    (scan_id, finding_id, title, description, severity, category,
                     affected_resource, evidence_json, recommendation, required_privilege, captured_at_utc)
                VALUES
                    ({1L}, {"WEC-SEC-TPM-NOT-RUN"}, {"TPM was not checked"}, {"Unknown state"},
                     {"Info"}, {"PlatformIntegrity"}, {"TPM"}, {"{}"}, {"Retry elevated"},
                     {"Administrator"}, {StartedAt.UtcTicks})
                """);
        }

        using (WecDbContext migrateContext = CreateContext())
        {
            await migrateContext.Database.MigrateAsync();
        }

        using WecDbContext readContext = CreateContext();
        var repository = new EfSecurityScanRepository(
            readContext, NullLogger<EfSecurityScanRepository>.Instance);
        SecurityScanResult? legacy = await repository.GetLatestScanAsync(
            "PC-LEGACY", CancellationToken.None);

        Assert.NotNull(legacy);
        Assert.Null(legacy.CoverageVersion);
        Assert.False(legacy.Coverage.IsKnown);
        Assert.False(legacy.Coverage.IsComplete);
        Assert.Empty(legacy.CheckResults);
        Assert.Equal("WEC-SEC-TPM-NOT-RUN", Assert.Single(legacy.Findings).FindingId);

        await using SqliteConnection connection = new($"Data Source={_databasePath}");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM security_check_results";
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task MalformedPersistedCheckStatus_IsVisibleAsFailedAndCoverageUnknown()
    {
        using WecDbContext context = CreateContext();
        await context.Database.MigrateAsync();
        var repository = new EfSecurityScanRepository(
            context, NullLogger<EfSecurityScanRepository>.Instance);
        await repository.SaveScanAsync(
            "PC-001",
            StartedAt,
            StartedAt.AddSeconds(1),
            ScanStatus.Completed,
            SecurityCoverage.CurrentVersion,
            [new SecurityCheckResult("FIREWALL", CheckStatus.Succeeded, [])],
            CancellationToken.None);
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE security_check_results SET status = 'FUTURE_UNKNOWN_STATUS'");
        context.ChangeTracker.Clear();

        SecurityScanResult? reloaded = await repository.GetLatestScanAsync(
            "PC-001", CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.False(reloaded.Coverage.IsKnown);
        SecurityCheckResult check = Assert.Single(reloaded.CheckResults);
        Assert.Equal(CheckStatus.Failed, check.Status);
    }

    [Fact]
    public async Task MalformedPersistedFindingEnums_AreVisibleAsUnknown()
    {
        using WecDbContext context = CreateContext();
        await context.Database.MigrateAsync();
        var repository = new EfSecurityScanRepository(
            context, NullLogger<EfSecurityScanRepository>.Instance);
        await repository.SaveScanAsync(
            "PC-001",
            StartedAt,
            StartedAt.AddSeconds(1),
            ScanStatus.Completed,
            [BuildFinding()],
            CancellationToken.None);
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE security_findings SET severity = 'FUTURE_SEVERITY', category = 'FUTURE_CATEGORY'");
        context.ChangeTracker.Clear();

        SecurityScanResult? reloaded = await repository.GetLatestScanAsync(
            "PC-001", CancellationToken.None);

        SecurityFinding finding = Assert.Single(reloaded!.Findings);
        Assert.Equal(FindingSeverity.Unknown, finding.Severity);
        Assert.Equal(FindingCategory.Unknown, finding.Category);
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
