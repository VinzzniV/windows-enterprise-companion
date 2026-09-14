using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Infrastructure.Persistence;
using Wec.Modules.Inventory.Persistence;
using Wec.Modules.Security.Persistence;
using Wec.Modules.Targets.Persistence;

namespace Wec.Infrastructure.IntegrationTests.Persistence;

public sealed class StoredDeviceListTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"wec-stored-list-{Guid.NewGuid():N}.db");
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BoundedListsReadOnlyAddressFieldsAndKeepSourceCountsNativeRecordsAndFullNames()
    {
        var commands = new Commands();
        using WecDbContext context = IntegrationDbContextFactory.Create(_path, commands);
        await context.Database.MigrateAsync();
        context.AddRange(
            new HardwareSnapshotRecord { Host = "pc.a.example", CapturedAtUtc = Now.AddDays(-1), PayloadJson = "{}" },
            new HardwareSnapshotRecord { Host = "pc.a.example", CapturedAtUtc = Now, PayloadJson = "unreadable but not needed for the list" },
            new HardwareSnapshotRecord { Host = "pc.a.example", CapturedAtUtc = Now, PayloadJson = "{}" },
            new HardwareSnapshotRecord { Host = "pc.b.example", CapturedAtUtc = Now, PayloadJson = "{}" },
            new HardwareSnapshotRecord { Host = "   ", CapturedAtUtc = Now, PayloadJson = "{}" },
            new SecurityScanRecord { Host = "192.0.2.10", StartedAtUtc = Now, CompletedAtUtc = Now, Status = "Completed" },
            new SecurityScanRecord { Host = "192.0.2.10", StartedAtUtc = Now, CompletedAtUtc = Now.AddMinutes(-1), Status = "Completed" },
            new SecurityScanRecord { Host = "192.0.2.11", StartedAtUtc = Now, CompletedAtUtc = Now, Status = "Completed" },
            new SecurityScanRecord { Host = "", StartedAtUtc = Now, CompletedAtUtc = Now, Status = "Completed" },
            new SavedTargetRecord { Host = "pc.c.example", Label = "Saved client", Role = TargetRoles.Client, CreatedAtUtc = Now },
            new SavedTargetRecord { Host = "print.example", Label = "Print server", Role = TargetRoles.PrintServer, CreatedAtUtc = Now });
        await context.SaveChangesAsync();
        IStoredDeviceListProvider[] providers = [new InventoryStoredDeviceListProvider(context), new SecurityStoredDeviceListProvider(context), new SavedClientListProvider(context)];
        int[] totals = [3, 2, 1];
        for (int index = 0; index < providers.Length; ++index)
        {
            commands.Text.Clear();
            Result<StoredDeviceAddressPage> result = await providers[index].ReadAsync(1, null, CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Equal(totals[index], result.Value.TotalRecords);
            Assert.Single(result.Value.Records);
            Assert.Equal(2, commands.Text.Count);
            Assert.All(commands.Text, text =>
            {
                Assert.DoesNotContain("payload_json", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("security_findings", text, StringComparison.OrdinalIgnoreCase);
            });
        }
        var inventory = (await providers[0].ReadAsync(100, "PC.A", CancellationToken.None)).Value;
        Assert.Equal(2, inventory.TotalRecords);
        Assert.Equal(2, inventory.Records.Select(row => row.RecordId).Distinct().Count());
        Assert.All(inventory.Records, row => { Assert.Equal("pc.a.example", row.Host); Assert.Equal(Now, row.ObservedAtUtc); });
        var security = (await providers[1].ReadAsync(100, "192.0.2.10", CancellationToken.None)).Value;
        Assert.Equal(Now.AddMinutes(-1), Assert.Single(security.Records).ObservedAtUtc);
        Assert.Empty((await providers[1].ReadAsync(100, "%", CancellationToken.None)).Value.Records);
        Assert.Single((await providers[2].ReadAsync(100, "saved", CancellationToken.None)).Value.Records);
        Assert.Equal(5, await context.Set<HardwareSnapshotRecord>().CountAsync());
        Assert.Equal(4, await context.Set<SecurityScanRecord>().CountAsync());
    }

    [Fact]
    public async Task StorageFailureIsTypedAndCancellationIsNotConvertedIntoEmptyData()
    {
        using WecDbContext context = IntegrationDbContextFactory.Create(_path);
        IStoredDeviceListProvider[] providers = [new InventoryStoredDeviceListProvider(context), new SecurityStoredDeviceListProvider(context), new SavedClientListProvider(context)];
        foreach (IStoredDeviceListProvider provider in providers)
        {
            Result<StoredDeviceAddressPage> result = await provider.ReadAsync(5, null, CancellationToken.None);
            Assert.Equal(ErrorCode.ServiceUnavailable, result.Error!.Code);
            Assert.Null(result.Error.Details);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.ReadAsync(5, null, new CancellationToken(true)));
        }
    }

    private sealed class Commands : DbCommandInterceptor
    {
        public List<string> Text { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Text.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) { File.Delete(_path); }
    }
}
