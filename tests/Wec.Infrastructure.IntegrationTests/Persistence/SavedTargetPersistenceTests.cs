using Microsoft.EntityFrameworkCore;
using Wec.Infrastructure.Persistence;
using Wec.Modules.Targets.Persistence;

namespace Wec.Infrastructure.IntegrationTests.Persistence;

public sealed class SavedTargetPersistenceTests : IDisposable
{
    private static readonly DateTimeOffset When =
        new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"wec-integration-{Guid.NewGuid():N}.db");

    private WecDbContext CreateContext() => IntegrationDbContextFactory.Create(_databasePath);

    [Fact]
    public async Task Upsert_IsKeyedOnHostAndRoleCaseInsensitively()
    {
        using (WecDbContext writeContext = CreateContext())
        {
            await writeContext.Database.MigrateAsync();
            var repository = new EfSavedTargetRepository(writeContext);
            await repository.UpsertAsync(
                "Print server", "PRSRV1", TargetRoles.PrintServer, "svc-print", When, CancellationToken.None);
            // Same host/role in different casing updates the existing row, no duplicate
            await repository.UpsertAsync(
                "Main print server", "prsrv1", TargetRoles.PrintServer, null, When, CancellationToken.None);
            // Same host, different role is a distinct saved target
            await repository.UpsertAsync(
                "PRSRV1 as DC", "PRSRV1", TargetRoles.DomainController, null, When, CancellationToken.None);
        }

        using (WecDbContext readContext = CreateContext())
        {
            var repository = new EfSavedTargetRepository(readContext);
            IReadOnlyList<SavedTarget> targets = await repository.ListAsync(CancellationToken.None);
            Assert.Equal(2, targets.Count);

            SavedTarget printServer = Assert.Single(targets, t => t.Role == TargetRoles.PrintServer);
            Assert.Equal("Main print server", printServer.Label);
            Assert.Equal("PRSRV1", printServer.Host); // original casing preserved
            Assert.Null(printServer.UserName); // password never stored; user name cleared on update
            Assert.Contains(targets, t => t.Role == TargetRoles.DomainController);
        }
    }

    [Fact]
    public async Task Delete_RemovesById()
    {
        int id;
        using (WecDbContext writeContext = CreateContext())
        {
            await writeContext.Database.MigrateAsync();
            var repository = new EfSavedTargetRepository(writeContext);
            SavedTarget saved = await repository.UpsertAsync(
                "opsi", "opsi.kauth.local", TargetRoles.OpsiServer, "adminuser", When, CancellationToken.None);
            id = saved.Id;
            Assert.True(id > 0);
            Assert.Equal("adminuser", saved.UserName);
        }

        using (WecDbContext deleteContext = CreateContext())
        {
            var repository = new EfSavedTargetRepository(deleteContext);
            await repository.DeleteAsync(id, CancellationToken.None);
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
