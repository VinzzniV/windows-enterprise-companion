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

    private static HardwareSnapshot BuildSnapshot() => new(
        new CpuInfo("Integration CPU", 8, 16, 3600),
        [new MemoryBank("IntRAM", "IR-1", 17179869184, 3200)],
        [new DiskDrive("Integration SSD", 512110190592, "SCSI", "Fixed hard disk media")],
        new OperatingSystemInfo("Windows 11 Pro", "10.0.26200", "26200", "64-bit"));

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
            await repository.SaveAsync(snapshot, capturedAtUtc, CancellationToken.None);
        }

        using WecDbContext readContext = CreateContext();
        var reloadedRepository = new EfHardwareSnapshotRepository(
            readContext, NullLogger<EfHardwareSnapshotRepository>.Instance);
        CachedHardwareSnapshot? reloaded = await reloadedRepository.GetLatestAsync(CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal(capturedAtUtc, reloaded.CapturedAtUtc);
        Assert.Equal(snapshot.Cpu, reloaded.Snapshot.Cpu);
        Assert.Equal(snapshot.OperatingSystem, reloaded.Snapshot.OperatingSystem);
        Assert.Equal(snapshot.MemoryBanks, reloaded.Snapshot.MemoryBanks);
        Assert.Equal(snapshot.Disks, reloaded.Snapshot.Disks);
    }

    [Fact]
    public async Task SaveAsync_ReplacesPreviousSnapshotInsteadOfAccumulating()
    {
        using WecDbContext context = CreateContext();
        await context.Database.MigrateAsync();
        var repository = new EfHardwareSnapshotRepository(
            context, NullLogger<EfHardwareSnapshotRepository>.Instance);

        await repository.SaveAsync(BuildSnapshot(), DateTimeOffset.UtcNow.AddHours(-1), CancellationToken.None);
        await repository.SaveAsync(BuildSnapshot(), DateTimeOffset.UtcNow, CancellationToken.None);

        int rowCount = await context.Set<HardwareSnapshotRecord>().CountAsync();
        Assert.Equal(1, rowCount);
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
