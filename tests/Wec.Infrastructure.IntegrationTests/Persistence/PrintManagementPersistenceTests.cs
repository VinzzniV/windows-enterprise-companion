using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wec.Infrastructure.Persistence;
using Wec.Modules.PrintManagement.Domain;
using Wec.Modules.PrintManagement.Persistence;

namespace Wec.Infrastructure.IntegrationTests.Persistence;

public sealed class PrintManagementPersistenceTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"wec-integration-{Guid.NewGuid():N}.db");

    private WecDbContext CreateContext() => IntegrationDbContextFactory.Create(_databasePath);

    private static EfPrintSnapshotRepository CreateRepository(WecDbContext context) =>
        new(context, NullLogger<EfPrintSnapshotRepository>.Instance);

    private static PrintServerSnapshot Snapshot(string server, DateTimeOffset capturedAt, string serial) =>
        new(server, capturedAt,
        [
            new PrinterEntry(
                "Queue-A", "PR-A", "Kyocera KX", "8.1.0.0", "IP_10.1.1.20", "10.1.1.20",
                "EG", null,
                new PrinterDevice(serial, "UTAX P-4539i MFP", "PR-EG", "Denkingen", "Idle", 1000,
                    [new TonerSupply("Toner Black", 80, false)]),
                null),
        ]);

    [Fact]
    public async Task Snapshot_SurvivesRoundTripWithHistoryNewestFirst()
    {
        var older = Snapshot("PRSRV1", new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero), "SER-1");
        var newer = Snapshot("PRSRV1", new DateTimeOffset(2026, 7, 3, 8, 0, 0, TimeSpan.Zero), "SER-2");

        using (WecDbContext writeContext = CreateContext())
        {
            await writeContext.Database.MigrateAsync();
            EfPrintSnapshotRepository repository = CreateRepository(writeContext);
            await repository.SaveAsync(older, historyLimit: 50, CancellationToken.None);
            await repository.SaveAsync(newer, historyLimit: 50, CancellationToken.None);
        }

        using WecDbContext readContext = CreateContext();
        EfPrintSnapshotRepository readRepository = CreateRepository(readContext);

        StoredPrintServer server = Assert.Single(
            await readRepository.ListServersAsync(CancellationToken.None));
        Assert.Equal("PRSRV1", server.Server);
        Assert.Equal(2, server.SnapshotCount);
        Assert.Equal(newer.CapturedAtUtc, server.CapturedAtUtc);

        PrintServerSnapshot? latest = await readRepository.GetLatestAsync("PRSRV1", CancellationToken.None);
        Assert.Equal("SER-2", Assert.Single(latest!.Printers).Device!.SerialNumber);

        IReadOnlyList<PrintSnapshotStamp> history =
            await readRepository.GetHistoryAsync("PRSRV1", CancellationToken.None);
        Assert.Equal(2, history.Count);
        Assert.True(history[0].CapturedAtUtc > history[1].CapturedAtUtc);

        PrintServerSnapshot? baseline = await readRepository.GetByIdAsync(history[1].Id, CancellationToken.None);
        Assert.Equal("SER-1", Assert.Single(baseline!.Printers).Device!.SerialNumber);
    }

    [Fact]
    public async Task Save_PrunesBeyondTheHistoryLimit()
    {
        using WecDbContext context = CreateContext();
        await context.Database.MigrateAsync();
        EfPrintSnapshotRepository repository = CreateRepository(context);
        for (int day = 1; day <= 4; day++)
        {
            await repository.SaveAsync(
                Snapshot("PRSRV1", new DateTimeOffset(2026, 7, day, 8, 0, 0, TimeSpan.Zero), $"SER-{day}"),
                historyLimit: 2,
                CancellationToken.None);
        }

        IReadOnlyList<PrintSnapshotStamp> history =
            await repository.GetHistoryAsync("PRSRV1", CancellationToken.None);
        Assert.Equal(2, history.Count);
        PrintServerSnapshot? latest = await repository.GetLatestAsync("PRSRV1", CancellationToken.None);
        Assert.Equal("SER-4", Assert.Single(latest!.Printers).Device!.SerialNumber);
    }

    [Fact]
    public async Task DeleteServer_RemovesAllItsSnapshotsOnly()
    {
        using WecDbContext context = CreateContext();
        await context.Database.MigrateAsync();
        EfPrintSnapshotRepository repository = CreateRepository(context);
        await repository.SaveAsync(
            Snapshot("PRSRV1", new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero), "SER-1"),
            historyLimit: 50, CancellationToken.None);
        await repository.SaveAsync(
            Snapshot("PRSRV2", new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero), "SER-2"),
            historyLimit: 50, CancellationToken.None);

        await repository.DeleteServerAsync("PRSRV1", CancellationToken.None);

        StoredPrintServer remaining = Assert.Single(
            await repository.ListServersAsync(CancellationToken.None));
        Assert.Equal("PRSRV2", remaining.Server);
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
