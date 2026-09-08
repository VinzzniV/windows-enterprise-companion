using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wec.Infrastructure.Persistence;
using Wec.Modules.Inventory.Domain;
using Wec.Modules.Inventory.Persistence;

namespace Wec.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Runs the real migrations against a temp SQLite file — never the in-memory
/// provider, because SQLite type/translation behavior is part of what we test.
/// </summary>
public sealed class HardwareSnapshotPersistenceTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"wec-integration-{Guid.NewGuid():N}.db");

    private WecDbContext CreateContext() => IntegrationDbContextFactory.Create(_databasePath);

    private static HardwareSnapshot BuildSnapshot(bool includeUserEvidence = true) => new(
        new CpuInfo("Integration CPU", 8, 16, 3600),
        [new MemoryBank("IntRAM", "IR-1", 17179869184, 3200)],
        [new DiskDrive("Integration SSD", 512110190592, "SCSI", "Fixed hard disk media")],
        new OperatingSystemInfo("Windows 11 Pro", "10.0.26200", "26200", "64-bit"),
        UserEvidence: includeUserEvidence
            ? new DeviceUserEvidence(
                UserEvidenceSourceState.Available,
                new InteractiveDomainUserEvidence("S-1-5-21-1-2-3-1104", "CORP", "alex"),
                null,
                UserEvidenceSourceState.Available,
                [new LocalUserProfileEvidence("S-1-5-21-1-2-3-1104", new DateTimeOffset(2026, 8, 27, 8, 0, 0, TimeSpan.Zero))],
                null,
                LocalProfilesTruncated: false)
            : null);

    [Fact]
    public async Task Migrations_CreateModulePrefixedTable()
    {
        using (WecDbContext context = CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
        var tables = new List<string>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Contains("inventory_hardware_snapshots", tables);
        Assert.Contains("__EFMigrationsHistory", tables);
    }

    [Fact]
    public async Task Snapshot_SurvivesRoundTripThroughRealMigrationAndNewContext()
    {
        HardwareSnapshot snapshot = BuildSnapshot();
        var capturedAtUtc = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);

        using (WecDbContext writeContext = CreateContext())
        {
            await writeContext.Database.MigrateAsync();
            var repository = new EfHardwareSnapshotRepository(
                writeContext, NullLogger<EfHardwareSnapshotRepository>.Instance);
            await repository.SaveAsync("PC-001", snapshot, capturedAtUtc, CancellationToken.None);
        }

        using WecDbContext readContext = CreateContext();
        var reloadedRepository = new EfHardwareSnapshotRepository(
            readContext, NullLogger<EfHardwareSnapshotRepository>.Instance);
        CachedHardwareSnapshot? reloaded = await reloadedRepository.GetLatestAsync("PC-001", CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal(capturedAtUtc, reloaded.CapturedAtUtc);
        Assert.Equal(snapshot.Cpu, reloaded.Snapshot.Cpu);
        Assert.Equal(snapshot.OperatingSystem, reloaded.Snapshot.OperatingSystem);
        Assert.Equal(snapshot.MemoryBanks, reloaded.Snapshot.MemoryBanks);
        Assert.Equal(snapshot.Disks, reloaded.Snapshot.Disks);
        Assert.Equal(snapshot.UserEvidence!.InteractiveUser, reloaded.Snapshot.UserEvidence!.InteractiveUser);
        Assert.Equal(snapshot.UserEvidence.LocalProfiles, reloaded.Snapshot.UserEvidence.LocalProfiles);
        Assert.Equal(snapshot.UserEvidence.LocalProfilesState, reloaded.Snapshot.UserEvidence.LocalProfilesState);
    }

    [Fact]
    public async Task SnapshotWithoutUserEvidenceProperty_RemainsReadableAsNotCaptured()
    {
        using WecDbContext context = CreateContext();
        await context.Database.MigrateAsync();
        string legacyPayload = JsonSerializer.Serialize(BuildSnapshot(includeUserEvidence: false))
            .Replace(",\"UserEvidence\":null", string.Empty, StringComparison.Ordinal);
        context.Set<HardwareSnapshotRecord>().Add(new HardwareSnapshotRecord
        {
            Host = "PC-LEGACY",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            PayloadJson = legacyPayload,
        });
        await context.SaveChangesAsync();
        var repository = new EfHardwareSnapshotRepository(
            context, NullLogger<EfHardwareSnapshotRepository>.Instance);

        CachedHardwareSnapshot? reloaded = await repository.GetLatestAsync(
            "PC-LEGACY",
            CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Null(reloaded.Snapshot.UserEvidence);
    }

    [Fact]
    public async Task SaveAsync_ReplacesPreviousSnapshotPerHostInsteadOfAccumulating()
    {
        using WecDbContext context = CreateContext();
        await context.Database.MigrateAsync();
        var repository = new EfHardwareSnapshotRepository(
            context, NullLogger<EfHardwareSnapshotRepository>.Instance);

        await repository.SaveAsync("PC-001", BuildSnapshot(), DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None);
        await repository.SaveAsync("PC-001", BuildSnapshot(), DateTimeOffset.UtcNow, CancellationToken.None);
        await repository.SaveAsync("PC-002", BuildSnapshot(), DateTimeOffset.UtcNow, CancellationToken.None);

        int totalRows = await context.Set<HardwareSnapshotRecord>().CountAsync();
        int firstHostRows = await context.Set<HardwareSnapshotRecord>()
            .CountAsync(record => record.Host == "PC-001");
        Assert.Equal(2, totalRows);
        Assert.Equal(1, firstHostRows);
    }

    [Fact]
    public async Task SaveAsync_KeepsTheNewerCaptureWhenAnOlderResultFinishesLater()
    {
        using WecDbContext context = CreateContext();
        await context.Database.MigrateAsync();
        var repository = new EfHardwareSnapshotRepository(
            context, NullLogger<EfHardwareSnapshotRepository>.Instance);
        DateTimeOffset newer = DateTimeOffset.UtcNow;

        await repository.SaveAsync("PC-ORDER", BuildSnapshot(), newer, CancellationToken.None);
        await repository.SaveAsync("pc-order.", BuildSnapshot(), newer.AddMinutes(-5), CancellationToken.None);

        CachedHardwareSnapshot? stored = await repository.GetLatestAsync("pc-order", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(newer, stored.CapturedAtUtc);
        Assert.Equal(1, await context.Set<HardwareSnapshotRecord>()
            .CountAsync(record => record.IdentityKey == "PC-ORDER"));
    }

    [Fact]
    public async Task SaveAsync_FailureDuringAtomicReplacementPreservesThePreviousSnapshot()
    {
        using WecDbContext context = CreateContext();
        await context.Database.MigrateAsync();
        var repository = new EfHardwareSnapshotRepository(
            context, NullLogger<EfHardwareSnapshotRepository>.Instance);
        DateTimeOffset originalCapturedAt = DateTimeOffset.UtcNow.AddHours(-1);
        DateTimeOffset rejectedCapturedAt = DateTimeOffset.UtcNow;
        await repository.SaveAsync("PC-ATOMIC", BuildSnapshot(), originalCapturedAt, CancellationToken.None);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_injected_inventory_capture
            BEFORE INSERT ON inventory_hardware_snapshots
            WHEN NEW.identity_key = 'PC-ATOMIC'
            BEGIN
                SELECT RAISE(ABORT, 'injected replacement failure');
            END;
            """);

        await Assert.ThrowsAsync<SqliteException>(() => repository.SaveAsync(
            "PC-ATOMIC", BuildSnapshot(), rejectedCapturedAt, CancellationToken.None));

        CachedHardwareSnapshot? stored = await repository.GetLatestAsync("PC-ATOMIC", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(originalCapturedAt, stored.CapturedAtUtc);
        Assert.Equal(1, await context.Set<HardwareSnapshotRecord>()
            .CountAsync(record => record.IdentityKey == "PC-ATOMIC"));
    }

    [Fact]
    public async Task ConcurrentSavesFromSeparateContextsLeaveOneNewestCurrentSnapshot()
    {
        using (WecDbContext migrationContext = CreateContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        DateTimeOffset older = DateTimeOffset.UtcNow.AddMinutes(-1);
        DateTimeOffset newer = DateTimeOffset.UtcNow;
        async Task SaveAsync(string host, DateTimeOffset capturedAtUtc)
        {
            using WecDbContext context = CreateContext();
            var repository = new EfHardwareSnapshotRepository(
                context, NullLogger<EfHardwareSnapshotRepository>.Instance);
            await repository.SaveAsync(host, BuildSnapshot(), capturedAtUtc, CancellationToken.None);
        }

        await Task.WhenAll(
            SaveAsync("pc-concurrent.corp.example", older),
            SaveAsync("PC-CONCURRENT.CORP.EXAMPLE.", newer));

        using WecDbContext verificationContext = CreateContext();
        HardwareSnapshotRecord current = Assert.Single(await verificationContext.Set<HardwareSnapshotRecord>()
            .Where(record => record.IdentityKey == "PC-CONCURRENT.CORP.EXAMPLE")
            .ToListAsync());
        Assert.Equal(newer, current.CapturedAtUtc);
    }

    [Fact]
    public async Task IdentityMigrationBackfillsUniqueRowsWithoutMergingLegacyDuplicates()
    {
        using (WecDbContext oldContext = CreateContext())
        {
            await oldContext.Database.MigrateAsync("20260826090316_ReplacePatchAutomationWithWinget");
            await oldContext.Database.ExecuteSqlRawAsync(
                "INSERT INTO inventory_hardware_snapshots (host, captured_at_utc, payload_json) VALUES ({0}, {1}, {2}), ({3}, {4}, {5}), ({6}, {7}, {8})",
                "UNIQUE.corp.example", DateTimeOffset.UtcNow.UtcTicks, "{}",
                "DUPLICATE", DateTimeOffset.UtcNow.UtcTicks, "{}",
                "duplicate", DateTimeOffset.UtcNow.UtcTicks, "{}");
        }

        using WecDbContext migratedContext = CreateContext();
        await migratedContext.Database.MigrateAsync();
        List<HardwareSnapshotRecord> rows = await migratedContext.Set<HardwareSnapshotRecord>()
            .OrderBy(record => record.Id)
            .ToListAsync();

        Assert.Equal("UNIQUE.CORP.EXAMPLE", rows.Single(record => record.Host.StartsWith("UNIQUE", StringComparison.Ordinal)).IdentityKey);
        Assert.All(rows.Where(record => record.Host.StartsWith("DUPLICATE", StringComparison.OrdinalIgnoreCase)),
            record => Assert.Null(record.IdentityKey));
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public async Task ListHostsAsync_ReturnsAllStoredHosts_AndDeleteRemovesOne()
    {
        using WecDbContext context = CreateContext();
        await context.Database.MigrateAsync();
        var repository = new EfHardwareSnapshotRepository(
            context, NullLogger<EfHardwareSnapshotRepository>.Instance);
        await repository.SaveAsync("PC-001", BuildSnapshot(), DateTimeOffset.UtcNow, CancellationToken.None);
        await repository.SaveAsync("PC-002", BuildSnapshot(), DateTimeOffset.UtcNow, CancellationToken.None);

        IReadOnlyList<StoredInventoryHost> hosts = await repository.ListHostsAsync(CancellationToken.None);
        Assert.Equal(["PC-001", "PC-002"], hosts.Select(host => host.Host).ToArray());

        await repository.DeleteAsync("PC-001", CancellationToken.None);

        IReadOnlyList<StoredInventoryHost> remaining = await repository.ListHostsAsync(CancellationToken.None);
        Assert.Equal("PC-002", Assert.Single(remaining).Host);
    }

    [Fact]
    public async Task ListHostsAsync_FiltersLegacySnapshotsWithoutMutatingThem()
    {
        using WecDbContext context = CreateContext();
        await context.Database.MigrateAsync();
        context.Set<HardwareSnapshotRecord>().AddRange(
            new HardwareSnapshotRecord
            {
                Host = string.Empty,
                CapturedAtUtc = DateTimeOffset.UtcNow,
                PayloadJson = "{}",
            },
            new HardwareSnapshotRecord
            {
                Host = "   ",
                CapturedAtUtc = DateTimeOffset.UtcNow,
                PayloadJson = "{}",
            });
        await context.SaveChangesAsync();
        var repository = new EfHardwareSnapshotRepository(
            context, NullLogger<EfHardwareSnapshotRepository>.Instance);

        IReadOnlyList<StoredInventoryHost> hosts = await repository.ListHostsAsync(CancellationToken.None);

        Assert.Empty(hosts);
        Assert.Equal(2, await context.Set<HardwareSnapshotRecord>().CountAsync());
    }

    [Fact]
    public async Task GetLatestAsync_ForUnknownHost_ReturnsNull()
    {
        using WecDbContext context = CreateContext();
        await context.Database.MigrateAsync();
        var repository = new EfHardwareSnapshotRepository(
            context, NullLogger<EfHardwareSnapshotRepository>.Instance);
        await repository.SaveAsync("PC-001", BuildSnapshot(), DateTimeOffset.UtcNow, CancellationToken.None);

        CachedHardwareSnapshot? other = await repository.GetLatestAsync("PC-999", CancellationToken.None);

        Assert.Null(other);
    }

    [Fact]
    public async Task GetLatestAsync_WithDamagedPayload_DoesNotReportMissing()
    {
        using WecDbContext context = CreateContext();
        await context.Database.MigrateAsync();
        context.Set<HardwareSnapshotRecord>().Add(new HardwareSnapshotRecord
        {
            Host = "PC-DAMAGED",
            IdentityKey = "PC-DAMAGED",
            CapturedAtUtc = DateTimeOffset.UtcNow,
            PayloadJson = "{not-json",
        });
        await context.SaveChangesAsync();
        var repository = new EfHardwareSnapshotRepository(
            context, NullLogger<EfHardwareSnapshotRepository>.Instance);

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.GetLatestAsync(
            "PC-DAMAGED",
            CancellationToken.None));
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
