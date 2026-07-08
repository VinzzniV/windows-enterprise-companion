using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wec.Infrastructure.Persistence;
using Wec.Modules.Diagnostics.Domain;
using Wec.Modules.Diagnostics.Persistence;
using Wec.Modules.PrintManagement.Domain;
using Wec.Modules.PrintManagement.Persistence;

namespace Wec.Infrastructure.IntegrationTests.Persistence;

public sealed class DiagnosticAndClientPrinterPersistenceTests : IDisposable
{
    private static readonly DateTimeOffset When = new(2026, 7, 8, 10, 0, 0, TimeSpan.Zero);

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"wec-integration-{Guid.NewGuid():N}.db");

    private WecDbContext CreateContext() => IntegrationDbContextFactory.Create(_databasePath);

    [Fact]
    public async Task DiagnosticRun_RoundTripsAndReplacesPerHost()
    {
        using (WecDbContext writeContext = CreateContext())
        {
            await writeContext.Database.MigrateAsync();
            var repository = new EfDiagnosticRunRepository(writeContext, NullLogger<EfDiagnosticRunRepository>.Instance);
            await repository.SaveAsync("PC-1", new DiagnosticRunResult(When, When, []), CancellationToken.None);
            // Newer run for the same host replaces the previous one (latest-per-host)
            await repository.SaveAsync(
                "PC-1", new DiagnosticRunResult(When.AddMinutes(5), When.AddMinutes(6), []), CancellationToken.None);
        }

        using (WecDbContext readContext = CreateContext())
        {
            var repository = new EfDiagnosticRunRepository(readContext, NullLogger<EfDiagnosticRunRepository>.Instance);
            DiagnosticRunResult? run = await repository.GetLatestAsync("PC-1", CancellationToken.None);
            Assert.NotNull(run);
            Assert.Equal(When.AddMinutes(6), run!.CompletedAtUtc);
            Assert.Null(await repository.GetLatestAsync("OTHER", CancellationToken.None));
        }
    }

    [Fact]
    public async Task ClientPrinterScan_RoundTripsAndReplacesPerHost()
    {
        var printer = new ClientPrinter("Reception", "Kyocera KX", "IP_10.0.0.5", "EG", false, true);

        using (WecDbContext writeContext = CreateContext())
        {
            await writeContext.Database.MigrateAsync();
            var repository = new EfClientPrinterScanRepository(
                writeContext, NullLogger<EfClientPrinterScanRepository>.Instance);
            await repository.SaveAsync(new ClientPrinterScan("PC-2", When, [printer]), CancellationToken.None);
            await repository.SaveAsync(
                new ClientPrinterScan("PC-2", When.AddMinutes(5), []), CancellationToken.None);
        }

        using (WecDbContext readContext = CreateContext())
        {
            var repository = new EfClientPrinterScanRepository(
                readContext, NullLogger<EfClientPrinterScanRepository>.Instance);
            ClientPrinterScan? scan = await repository.GetLatestAsync("PC-2", CancellationToken.None);
            Assert.NotNull(scan);
            Assert.Equal(When.AddMinutes(5), scan!.CapturedAtUtc);
            Assert.Empty(scan.Printers); // replaced, not accumulated
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
