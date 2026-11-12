using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Contracts;
using Wec.Core.Objects;
using Wec.Core.Results;
using Wec.Modules.Clients.Application;

namespace Wec.Modules.Clients.Tests.Application;

public sealed class ManagementDeviceObjectServiceTests
{
    private readonly IManagementDeviceSnapshotProvider _provider = Substitute.For<IManagementDeviceSnapshotProvider>();
    private static readonly Guid SnapshotId = Guid.NewGuid();
    private static readonly Guid DirectoryId = Guid.NewGuid();
    private ManagementDeviceObjectService Service(int limit = 100) => new(_provider, new("workspace", "LOCAL"), Options.Create(new ObjectWorkingSetOptions { MaximumRecords = limit }));
    private static ManagementDeviceSnapshot Snapshot() => new(DateTimeOffset.UtcNow,
        [new("ActiveDirectory", "example.test", "Partial", null, 2), new("Kaspersky", "ksc.example:13299", "Available", null, 2),
            new("Opsi", "https://opsi.example:4447", "Available", null, 1), new("Nessus", null, "Available", null, 1)],
        [new("PC", "pc.example.test", "Windows", null, null, "CN=PC", null, DirectoryId, null, "example.test"),
            new("PC", "pc.other.test", null, null, false, "CN=Other", null)],
        [new("PC", "pc.example.test", null, "native-one", null, "1", "2", null), new("PC", "pc.example.test", null, "native-two", null, "3", "4", null)],
        [new("opsi-only.example.test", "Unmatched opsi device", "depot", null, "1")],
        [new("10.23.45.67", "legacy-id", "10.23.45.67", null, 1, 2, 3, 4, 5, [443], ["scan"], "10.23.45.67|NESSUS:native", null, "host-uuid", "bios-uuid")])
    { SnapshotId = SnapshotId, SessionRevision = 1, Revision = 2 };

    private void Cached(ManagementDeviceSnapshot? snapshot) => _provider.ReadCachedAsync(Arg.Any<DirectoryInventoryConnection?>(),
        Arg.Any<KasperskyInventoryConnection?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(snapshot));

    [Fact]
    public async Task MinimalCachedListKeepsSourceOnlyRecordsNativeIdsUnknownsAndDuplicateLocators()
    {
        Cached(Snapshot());
        var list = (await Service().ListCachedAsync(new(), CancellationToken.None)).Value;
        Assert.Equal(6, list.Reads.Sum(read => read.Rows.Count));
        Assert.Equal(DirectoryId.ToString("D"), list.Reads[0].Rows[0].NativeReference!.Id);
        Assert.Null(list.Reads[0].Rows[0].AccountEnabled);
        Assert.Null(list.Reads[0].Rows[1].NativeReference);
        Assert.NotEqual(list.Reads[1].Rows[0].Reference, list.Reads[1].Rows[1].Reference);
        Assert.Equal("opsi-only.example.test", Assert.Single(list.Reads[2].Rows).Label);
        Assert.Equal("10.23.45.67", Assert.Single(list.Reads[3].Rows).Label);
        Assert.Null(list.Reads[3].State.Scope);
        await _provider.Received(1).ReadCachedAsync(null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordBoundIsSharedAcrossSourcesAndTargetedSearchRetainsOriginalRowIndex()
    {
        Cached(Snapshot());
        var bounded = (await Service(4).ListCachedAsync(new(), CancellationToken.None)).Value;
        Assert.All(bounded.Reads, read => Assert.Single(read.Rows));
        Assert.True(bounded.Reads[0].Limited);
        Assert.Equal(2, bounded.Reads[0].MatchingCachedRecords);
        var selected = (await Service(1).ListCachedAsync(new(Search: "native-two"), CancellationToken.None)).Value;
        ManagementDeviceListRow row = Assert.Single(selected.Reads.SelectMany(read => read.Rows));
        Assert.Equal(1, row.Reference.RecordIndex);
        var detail = (await Service().ReadCachedAsync(new(row.Reference), CancellationToken.None)).Value;
        Assert.Equal("native-two", detail.Kaspersky!.RecordName);
        Assert.Null(detail.ActiveDirectory);
        Assert.Null(detail.Nessus);
    }

    [Fact]
    public async Task FailedSourceAndMissingCacheHaveUnknownCountsRatherThanEmptySuccessfulInventories()
    {
        var snapshot = Snapshot();
        Cached(snapshot with { Sources = snapshot.Sources.Select(state => state.Source == "Opsi"
            ? state with { Availability = "Unavailable", Error = "Connection failed", LoadedRecords = 0 } : state).ToArray(), Opsi = [] });
        var list = (await Service().ListCachedAsync(new(), CancellationToken.None)).Value;
        Assert.Null(list.Reads[2].MatchingCachedRecords);
        Assert.Equal("Connection failed", list.Reads[2].State.Error);
        Assert.Single(list.Reads[3].Rows);
        Cached(null);
        var missing = (await Service().ListCachedAsync(new(), CancellationToken.None)).Value;
        Assert.All(missing.Reads, read => { Assert.Null(read.MatchingCachedRecords); Assert.Empty(read.Rows); Assert.Equal("NotLoaded", read.State.Availability); });
    }

    [Fact]
    public async Task ExpiredOrWrongWorkspaceLocatorsNeverOpenAnotherRecordWithTheSameNameOrIndex()
    {
        Cached(Snapshot());
        var reference = new ManagementDeviceRecordReference("different-workspace", SnapshotId, ManagementDeviceSource.Kaspersky, 0);
        Assert.Equal(ErrorCode.InvalidRequest, (await Service().ReadCachedAsync(new(reference), CancellationToken.None)).Error!.Code);
        Assert.Empty(_provider.ReceivedCalls());
        reference = reference with { Workspace = "workspace", SnapshotId = Guid.NewGuid() };
        Assert.Equal(ErrorCode.NotFound, (await Service().ReadCachedAsync(new(reference), CancellationToken.None)).Error!.Code);
        reference = reference with { SnapshotId = SnapshotId, RecordIndex = 2 };
        Assert.Equal(ErrorCode.NotFound, (await Service().ReadCachedAsync(new(reference), CancellationToken.None)).Error!.Code);
        Cached(null);
        Assert.Equal(ErrorCode.NotFound, (await Service().ReadCachedAsync(new(reference with { RecordIndex = 0 }), CancellationToken.None)).Error!.Code);
    }

    [Fact]
    public async Task CancellationAndInvalidInputDoNotProduceUsableSourceRecordLinks()
    {
        Cached(Snapshot());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().ListCachedAsync(new(), cancellation.Token));
        Assert.Equal(ErrorCode.InvalidRequest, (await Service().ListCachedAsync(new(Search: "bad\0input"), CancellationToken.None)).Error!.Code);
    }
}
