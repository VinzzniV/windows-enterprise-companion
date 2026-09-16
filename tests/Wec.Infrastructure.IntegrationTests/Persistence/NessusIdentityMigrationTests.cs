using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Wec.Infrastructure.Persistence;
using Wec.Modules.VulnerabilityManagement.Persistence;

namespace Wec.Infrastructure.IntegrationTests.Persistence;

public sealed class NessusIdentityMigrationTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"wec-nessus-identity-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task AdditiveMigrationPreservesLegacyKeysAndUnknownUuidProvenance()
    {
        await using (WecDbContext context = IntegrationDbContextFactory.Create(_path))
        {
            string[] migrations = context.Database.GetMigrations().ToArray();
            Assert.EndsWith("PreserveNessusIdentityProvenance", migrations[^1], StringComparison.Ordinal);
            await context.GetService<IMigrator>().MigrateAsync(migrations[^2]);
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO nessus_assets
                (asset_key, display_name, host_name, asset_id, last_scan_utc, critical, high, medium, low, info, ports_json, scan_sources_json)
                VALUES ('PC01', 'Historical device', 'PC01', 'legacy-unspecified-uuid', 0, 0, 0, 0, 0, 0, '[]', '[]')
                """);
            await context.Database.MigrateAsync();
            NessusAssetRecord legacy = await context.Set<NessusAssetRecord>().SingleAsync();
            Assert.Equal("PC01", legacy.AssetKey);
            Assert.Equal("legacy-unspecified-uuid", legacy.AssetId);
            Assert.Null(legacy.HostUuid);
            Assert.Null(legacy.BiosUuid);
            legacy.HostUuid = "source-host-id";
            legacy.BiosUuid = "source-bios-id";
            await context.SaveChangesAsync();
        }
        await using WecDbContext read = IntegrationDbContextFactory.Create(_path);
        NessusAssetRecord stored = await read.Set<NessusAssetRecord>().SingleAsync();
        Assert.Equal("source-host-id", stored.HostUuid);
        Assert.Equal("source-bios-id", stored.BiosUuid);
        Assert.False(read.Database.HasPendingModelChanges());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }
}
