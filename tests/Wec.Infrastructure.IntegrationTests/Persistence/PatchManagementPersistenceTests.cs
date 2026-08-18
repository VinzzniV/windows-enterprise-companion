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
    public async Task Mapping_UpsertRoundTripAndDelete()
    {
        using (WecDbContext writeContext = CreateContext())
        {
            await writeContext.Database.MigrateAsync();
            var repository = new EfPatchMappingRepository(writeContext);
            await repository.UpsertAsync("Mozilla Firefox", "firefox", CancellationToken.None);
            await repository.UpsertAsync("Mozilla Firefox", "firefox-esr", CancellationToken.None);
        }

        using (WecDbContext readContext = CreateContext())
        {
            var repository = new EfPatchMappingRepository(readContext);
            ProductMapping mapping = Assert.Single(await repository.ListAsync(CancellationToken.None));
            Assert.Equal("Mozilla Firefox", mapping.SoftwareName);
            Assert.Equal("firefox-esr", mapping.OpsiProductId);

            await repository.DeleteAsync("Mozilla Firefox", CancellationToken.None);
            Assert.Empty(await repository.ListAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task AuditEntries_RoundTripNewestFirst()
    {
        var older = new PatchAuditEntry(
            0, new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero), "vinz",
            "ROLLOUT_REQUESTED", "firefox", "depot-denkingen.kauth.local",
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
                "firefox", "ROLLOUT_REQUESTED", CancellationToken.None);
            Assert.NotNull(latest);
            Assert.Equal(entries[0].TimestampUtc, latest.TimestampUtc);
        }
    }

    [Fact]
    public async Task VersionSource_UpsertRoundTripAndDelete()
    {
        DateTimeOffset checkedAt = new(2026, 8, 14, 7, 0, 0, TimeSpan.Zero);
        using (WecDbContext writeContext = CreateContext())
        {
            await writeContext.Database.MigrateAsync();
            var repository = new EfProductVersionSourceRepository(writeContext);
            await repository.UpsertAsync(new ProductVersionSource(
                "firefox",
                "https://example.test/releases",
                "version ([0-9.]+)",
                Enabled: true,
                "129.0",
                checkedAt,
                "SUCCESS",
                LastError: null), CancellationToken.None);
        }

        using (WecDbContext readContext = CreateContext())
        {
            var repository = new EfProductVersionSourceRepository(readContext);
            ProductVersionSource source = Assert.Single(await repository.ListAsync(CancellationToken.None));
            Assert.Equal("129.0", source.LatestVersion);
            Assert.Equal(checkedAt, source.LastCheckedUtc);

            await repository.DeleteAsync("firefox", CancellationToken.None);
            Assert.Empty(await repository.ListAsync(CancellationToken.None));
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
