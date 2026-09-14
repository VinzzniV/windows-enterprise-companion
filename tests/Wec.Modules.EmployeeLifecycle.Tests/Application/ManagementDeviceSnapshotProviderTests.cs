using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Modules.EmployeeLifecycle.Application;

namespace Wec.Modules.EmployeeLifecycle.Tests.Application;

public sealed class ManagementDeviceSnapshotProviderTests
{
    [Fact]
    public void ProjectionKeepsDuplicatesNativeNamesAndSourceOnlyRecords()
    {
        var unavailable = new InventorySourceState(InventorySourceAvailability.Unavailable, "Read denied.");
        var available = new InventorySourceState(InventorySourceAvailability.Available);
        AdComputerInventoryItem first = new("PC01", "pc01.a.example", null, null, null, "CN=PC01,DC=a,DC=example", null, Guid.NewGuid());
        AdComputerInventoryItem recreated = first with { ObjectId = Guid.NewGuid() };
        var load = new HygieneSourceLoad(
            Result.Success(new AdComputerInventory(true, "a.example", [first, recreated], false)),
            Result.Success(new KasperskyInventory([
                new KasperskyComputer("PC01", null, null, null, null, "pc01.b.example", "PC01", "record-one"),
                new KasperskyComputer("PC01", null, null, null, null, "pc01.b.example", "PC01", "record-two"),
            ], false)),
            Result.Failure<OpsiComputerInventory>(new Error(ErrorCode.AccessDenied, "Read denied.")),
            Result.Success(new NessusComputerInventory([
                new NessusComputerInventoryItem("NESSUS-ONLY", "asset-native", "10.1.2.3", null, 0, 0, 0, 0, 0, [], []),
            ], NessusInventoryAvailability.Available, null)),
            new EnvironmentSourceStates(available, available, unavailable, available), "a.example");

        ManagementDeviceSnapshot snapshot = ManagementDeviceSnapshotProvider.Project(load, DateTimeOffset.UnixEpoch,
            new ItHygieneRequest(Kaspersky: new KasperskyInventoryConnection(Server: "https://user:password@ksc.example:13299")),
            new ItLifecycleOptions());

        Assert.Equal(2, snapshot.ActiveDirectory.Count);
        Assert.NotEqual(snapshot.ActiveDirectory[0].ObjectId, snapshot.ActiveDirectory[1].ObjectId);
        Assert.Equal(2, snapshot.Kaspersky.Count);
        Assert.Equal("pc01.b.example", snapshot.Kaspersky[0].Fqdn);
        Assert.Equal("record-two", snapshot.Kaspersky[1].RecordName);
        Assert.Equal("asset-native", Assert.Single(snapshot.Nessus).AssetId);
        Assert.Equal("ksc.example:13299", snapshot.Sources[1].Scope);
        Assert.Equal("Unavailable", snapshot.Sources[2].Availability);
        Assert.Empty(snapshot.Opsi);
    }

    [Fact]
    public async Task CachedReadsAreIsolatedByCredentialsAndNeverLoadMissingSources()
    {
        using var cache = new ItHygieneSnapshotCache();
        var provider = new ManagementDeviceSnapshotProvider(cache);
        var connection = new DirectoryInventoryConnection(Domain: "a.example", UserName: "reader", Password: "test-a");
        var snapshot = new ManagementDeviceSnapshot(DateTimeOffset.UnixEpoch, [], [], [], [], []);
        int calls = 0;
        Task<Result<ItHygieneResult>> Load(ItHygieneRequest _, CancellationToken __)
        {
            calls++;
            var available = new InventorySourceState(InventorySourceAvailability.Available);
            return Task.FromResult(Result.Success(new ItHygieneResult(DateTimeOffset.UnixEpoch, "a.example",
                new EnvironmentSourceStates(available, available, available), ItHygieneService.Summarize([]), [])
            {
                SourceRecords = snapshot,
            }));
        }

        Assert.Null(await provider.ReadCachedAsync(connection, null, CancellationToken.None));
        await cache.GetAsync(new ItHygieneRequest(connection), false, Load, CancellationToken.None);
        var cached = await provider.ReadCachedAsync(connection, null, CancellationToken.None);
        Assert.Same(snapshot.ActiveDirectory, cached!.ActiveDirectory);
        Assert.True(cached.SessionRevision > 0);
        Assert.True(cached.Revision > cached.SessionRevision);
        Assert.Null(await provider.ReadCachedAsync(connection with { Password = "test-b" }, null, CancellationToken.None));
        Assert.Equal(1, calls);

        await cache.GetAsync(new ItHygieneRequest(connection with { Domain = "b.example" }), false,
            (_, _) => Task.FromResult(Result.Failure<ItHygieneResult>(new Error(ErrorCode.AccessDenied, "Read denied."))), CancellationToken.None);
        Assert.Null(await provider.ReadCachedAsync(connection, null, CancellationToken.None));
    }

    [Fact]
    public async Task CachedProjectionDoesNotWaitForSourceIoAndContextChangeRejectsLateCompletion()
    {
        using var cache = new ItHygieneSnapshotCache();
        var completion = new TaskCompletionSource<Result<ItHygieneResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new ItHygieneRequest(new DirectoryInventoryConnection(Domain: "a.example"));
        var loading = cache.GetAsync(request, true, (_, _) => completion.Task, CancellationToken.None);
        Assert.Null(await cache.ReadCachedAsync(request, CancellationToken.None));
        var other = new ItHygieneRequest(new DirectoryInventoryConnection(Domain: "b.example"));
        Assert.Null(await cache.ReadCachedAsync(other, CancellationToken.None));
        completion.SetResult(Result.Success(SourceResult()));
        Assert.True((await loading).IsFailure);
        Assert.Null(await cache.ReadCachedAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task FailedKasperskySourceDoesNotHideOtherRawSourceEvidenceOrBecomeALegacyCacheHit()
    {
        using var cache = new ItHygieneSnapshotCache();
        int calls = 0;
        Task<Result<ItHygieneResult>> Load(ItHygieneRequest _, CancellationToken __)
        {
            calls++; return Task.FromResult(Result.Success(SourceResult(true)));
        }
        var request = new ItHygieneRequest();
        await cache.GetAsync(request, false, Load, CancellationToken.None);
        var first = await cache.ReadCachedAsync(request, CancellationToken.None);
        Assert.NotNull(first);
        Assert.Single(first.ActiveDirectory);
        await cache.GetAsync(request, false, Load, CancellationToken.None);
        Assert.Equal(2, calls);
        Assert.True((await cache.ReadCachedAsync(request, CancellationToken.None))!.Revision > first.Revision);
    }

    private static ItHygieneResult SourceResult(bool kscFailed = false)
    {
        var available = new InventorySourceState(InventorySourceAvailability.Available);
        return new(DateTimeOffset.UnixEpoch, "a.example", new EnvironmentSourceStates(available,
            kscFailed ? new(InventorySourceAvailability.Unavailable, "Denied") : available, available), ItHygieneService.Summarize([]), [])
        {
            SourceRecords = new(DateTimeOffset.UnixEpoch, [],
                [new("PC01", "pc01.a.example", null, null, null, "CN=PC01,DC=a,DC=example", null, Guid.NewGuid())], [], [], []),
        };
    }

    [Fact]
    public async Task CachedReadHonorsCancellation()
    {
        using var cache = new ItHygieneSnapshotCache();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ManagementDeviceSnapshotProvider(cache).ReadCachedAsync(null, null, cancellation.Token));
    }
}
