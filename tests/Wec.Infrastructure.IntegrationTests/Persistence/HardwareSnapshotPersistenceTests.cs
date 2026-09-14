using System.Text.Json;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    public async Task ListHostsAsync_HidesLegacySnapshotsWithoutDeletingThem()
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

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    public async Task BoundedEvidenceBatchPreservesLatestTiesAddressesAndUnreadableRowsWithoutPerHostQueries()
    {
        var counter = new ReadCommandCounter();
        using WecDbContext context = IntegrationDbContextFactory.Create(_databasePath, counter);
        await context.Database.MigrateAsync();
        DateTimeOffset now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
        string payload = JsonSerializer.Serialize(BuildSnapshot());
        context.AddRange(
            new HardwareSnapshotRecord { Host = "A.corp.example", CapturedAtUtc = now.AddDays(-1), PayloadJson = payload },
            new HardwareSnapshotRecord { Host = "A.corp.example", CapturedAtUtc = now, PayloadJson = payload },
            new HardwareSnapshotRecord { Host = "A.corp.example", CapturedAtUtc = now, PayloadJson = payload },
            new HardwareSnapshotRecord { Host = "A.other.example", CapturedAtUtc = now, PayloadJson = payload },
            new HardwareSnapshotRecord { Host = "192.0.2.10", CapturedAtUtc = now, PayloadJson = payload },
            new HardwareSnapshotRecord { Host = "C.legacy", CapturedAtUtc = now, PayloadJson = "{}" },
            new HardwareSnapshotRecord { Host = "D.bad", CapturedAtUtc = now, PayloadJson = "broken" },
            new HardwareSnapshotRecord { Host = "E.null", CapturedAtUtc = now, PayloadJson = "null" },
            new HardwareSnapshotRecord { Host = "   ", CapturedAtUtc = now, PayloadJson = payload });
        await context.SaveChangesAsync();
        var repository = new EfHardwareSnapshotRepository(context, NullLogger<EfHardwareSnapshotRepository>.Instance);
        counter.Count = 0;
        StoredInventoryUserEvidenceBatch bounded = await repository.GetLatestUserEvidenceBatchAsync(2, CancellationToken.None);
        Assert.Equal(3, counter.Count);
        Assert.Equal(6, bounded.StoredHostCount);
        Assert.Equal(7, bounded.LatestRecordCount);
        Assert.Equal(2, bounded.Records.Count);
        Assert.Equal("192.0.2.10", bounded.Records[0].Host);
        counter.Count = 0;
        StoredInventoryUserEvidenceBatch all = await repository.GetLatestUserEvidenceBatchAsync(100, CancellationToken.None);
        Assert.Equal(3, counter.Count);
        Assert.Equal(7, all.Records.Count);
        StoredInventoryUserEvidence[] tied = all.Records.Where(record => record.Host == "A.corp.example").ToArray();
        Assert.Equal(2, tied.Length);
        Assert.Equal(2, tied.Select(record => record.SnapshotId).Distinct().Count());
        Assert.All(tied, record => Assert.Equal(now, record.CapturedAtUtc));
        Assert.Equal(BuildSnapshot().UserEvidence!.InteractiveUser, tied[0].Evidence!.InteractiveUser);
        StoredInventoryUserEvidence legacy = Assert.Single(all.Records, record => record.Host == "C.legacy");
        Assert.True(legacy.Readable);
        Assert.Null(legacy.Evidence);
        Assert.False(Assert.Single(all.Records, record => record.Host == "D.bad").Readable);
        Assert.False(Assert.Single(all.Records, record => record.Host == "E.null").Readable);
        Assert.Equal(9, await context.Set<HardwareSnapshotRecord>().CountAsync());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.GetLatestUserEvidenceBatchAsync(0, CancellationToken.None));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetLatestUserEvidenceBatchAsync(10, new CancellationToken(true)));
    }

    private sealed class ReadCommandCounter : DbCommandInterceptor
    {
        public int Count { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ++Count;
            return ValueTask.FromResult(result);
        }
    }
}
