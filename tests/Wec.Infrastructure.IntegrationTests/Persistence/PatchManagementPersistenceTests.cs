using Microsoft.EntityFrameworkCore;
using Wec.Infrastructure.Persistence;
using Wec.Modules.PatchManagement.Persistence;

namespace Wec.Infrastructure.IntegrationTests.Persistence;

public sealed class PatchManagementPersistenceTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"wec-integration-{Guid.NewGuid():N}.db");

    private WecDbContext CreateContext() => IntegrationDbContextFactory.Create(_databasePath);

    [Fact]
    public async Task WingetPackage_UpsertRoundTrip()
    {
        DateTimeOffset now = new(2026, 8, 26, 8, 0, 0, TimeSpan.Zero);
        using (WecDbContext writeContext = CreateContext())
        {
            await writeContext.Database.MigrateAsync();
            var repository = new EfWingetManagedPackageRepository(writeContext);
            await repository.UpsertAsync(new WingetManagedPackage(
                0, "7zip", "7zip.7zip", "winget", "machine", "depot.example.test", "7-Zip",
                "25.01", 1, "26.02", "SUCCESS", now, null, now, now), CancellationToken.None);
        }

        using (WecDbContext readContext = CreateContext())
        {
            var repository = new EfWingetManagedPackageRepository(readContext);
            WingetManagedPackage package = Assert.Single(await repository.ListAsync(CancellationToken.None));
            Assert.Equal("7zip.7zip", package.WingetId);
            Assert.Equal("26.02", package.LatestWingetVersion);
            Assert.Equal(now, package.CheckedAtUtc);
        }
    }

    [Fact]
    public async Task AuditEntries_RoundTripNewestFirst()
    {
        var older = new PatchAuditEntry(
            0, new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero), "vinz",
            "WINGET_PACKAGE_UPDATED", "firefox", "depot-denkingen.kauth.local",
            ["pc1.kauth.local", "pc2.kauth.local"], "{\"preview\":true}", "SUCCESS", null);
        var newer = older with
        {
            TimestampUtc = new DateTimeOffset(2026, 7, 3, 11, 0, 0, TimeSpan.Zero),
            Result = "FAILED",
            ErrorMessage = "opsi denied access.",
            OldVersion = "128.0-1",
            NewVersion = "129.0-1",
        };

        using (WecDbContext writeContext = CreateContext())
        {
            await writeContext.Database.MigrateAsync();
            var repository = new EfPatchAuditRepository(writeContext);
            await repository.AddAsync(older, CancellationToken.None);
            await repository.AddAsync(newer, CancellationToken.None);
        }

        using (WecDbContext readContext = CreateContext())
        {
            var repository = new EfPatchAuditRepository(readContext);
            IReadOnlyList<PatchAuditEntry> entries =
                await repository.ListAsync(10, CancellationToken.None);

            Assert.Equal(2, entries.Count);
            Assert.Equal("FAILED", entries[0].Result);
            Assert.Equal("opsi denied access.", entries[0].ErrorMessage);
            Assert.Equal("128.0-1", entries[0].OldVersion);
            Assert.Equal("129.0-1", entries[0].NewVersion);
            Assert.Equal(["pc1.kauth.local", "pc2.kauth.local"], entries[1].TargetClients);
            Assert.Equal("{\"preview\":true}", entries[1].PreviewJson);

            Assert.Single(await repository.ListAsync(1, CancellationToken.None));
            PatchAuditEntry? latest = await repository.FindLatestAsync(
                "firefox", "WINGET_PACKAGE_UPDATED", CancellationToken.None);
            Assert.NotNull(latest);
            Assert.Equal(entries[0].TimestampUtc, latest.TimestampUtc);
        }
    }

    [Fact]
    public async Task WingetMigration_DropsLegacyTablesAndPreservesAuditHistory()
    {
        using (WecDbContext previousContext = CreateContext())
        {
            await previousContext.Database.MigrateAsync("20260819054558_AddSecurityCheckCoverage");
            var audit = new EfPatchAuditRepository(previousContext);
            await audit.AddAsync(new PatchAuditEntry(
                0, new DateTimeOffset(2026, 8, 25, 8, 0, 0, TimeSpan.Zero), "admin",
                "HISTORICAL_ACTION", "7zip", "depot.example.test", [], null,
                "SUCCESS", null, "25.01-1", "25.01-2"), CancellationToken.None);
        }

        using (WecDbContext migratedContext = CreateContext())
        {
            await migratedContext.Database.MigrateAsync();
            Assert.False(await TableExistsAsync(migratedContext, "patchmanagement_product_mappings"));
            Assert.False(await TableExistsAsync(migratedContext, "patchmanagement_version_sources"));
            Assert.True(await TableExistsAsync(migratedContext, "patchmanagement_winget_packages"));

            var audit = new EfPatchAuditRepository(migratedContext);
            PatchAuditEntry entry = Assert.Single(await audit.ListAsync(10, CancellationToken.None));
            Assert.Equal("HISTORICAL_ACTION", entry.Action);
            Assert.Equal("25.01-1", entry.OldVersion);
        }
    }

    private static async Task<bool> TableExistsAsync(WecDbContext context, string tableName)
    {
        System.Data.Common.DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using System.Data.Common.DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        System.Data.Common.DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture) == 1;
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
