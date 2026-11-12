using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.ActiveDirectory.Application;

namespace Wec.Modules.ActiveDirectory.Tests.Application;

public sealed class DirectoryGroupSnapshotCacheTests
{
    private readonly IClock _clock = Substitute.For<IClock>();
    private static DirectoryUserReadConnection Connection => new("example.test", null, ScanCredentials.CurrentUser);
    private DirectoryGroupSnapshotCache Create(int limit = 64) => new(_clock, Options.Create(new ActiveDirectoryOptions { IdentityCacheMaximumEntries = limit }));
    public DirectoryGroupSnapshotCacheTests() => _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
    private Result<DirectoryGroupReadData> Empty() => Result.Success(new DirectoryGroupReadData(Identity: new("example.test", _clock.UtcNow, [], false)));

    [Fact]
    public async Task IndependentMemberFailureRetainsOriginalFactsWithoutExtendingRetention()
    {
        using var cache = Create();
        await cache.LoadAsync(Connection, "example.test", "identity:one", _ => Task.FromResult(Empty()), CancellationToken.None);
        var before = cache.Read(Connection, "example.test", "identity:one", CancellationToken.None);
        _clock.UtcNow.Returns(_clock.UtcNow.AddMinutes(59));
        var failure = Result.Failure<DirectoryGroupReadData>(new(ErrorCode.AccessDenied, "Denied"));
        await cache.LoadAsync(Connection, "example.test", "members:one", _ => Task.FromResult(failure), CancellationToken.None);
        Assert.Null(cache.Read(Connection, "example.test", "identity:one", CancellationToken.None).State.LastAttemptError);
        await cache.LoadAsync(Connection, "example.test", "identity:one", _ => Task.FromResult(failure), CancellationToken.None);
        var stale = cache.Read(Connection, "example.test", "identity:one", CancellationToken.None);
        Assert.Same(before.Data, stale.Data);
        Assert.Equal(before.State.RetainedUntilUtc, stale.State.RetainedUntilUtc);
        _clock.UtcNow.Returns(_clock.UtcNow.AddMinutes(2));
        var expired = cache.Read(Connection, "example.test", "identity:one", CancellationToken.None);
        Assert.Null(expired.Data.Identity);
        Assert.Equal(ErrorCode.AccessDenied, expired.State.LastAttemptError!.Code);
    }

    [Fact]
    public async Task ConnectionChangesInvalidateCachedEvidenceAndLateReads()
    {
        using var cache = Create();
        await cache.LoadAsync(Connection, "example.test", "identity:one", _ => Task.FromResult(Empty()), CancellationToken.None);
        var completion = new TaskCompletionSource<Result<DirectoryGroupReadData>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loading = cache.LoadAsync(Connection, "example.test", "identity:one", _ => completion.Task, CancellationToken.None);
        var changed = Connection with { Credentials = ScanCredentials.Explicit("test", "EXAMPLE", "test-only-placeholder") };
        Assert.Null(cache.Read(changed, "example.test", "identity:one", CancellationToken.None).Data.Identity);
        completion.SetResult(Empty());
        Assert.True((await loading).IsFailure);
        Assert.Null(cache.Read(Connection, "example.test", "identity:one", CancellationToken.None).Data.Identity);
    }

    [Fact]
    public async Task ConcurrentReadsCoalesceWhileCachedReadsStayImmediate()
    {
        using var cache = Create();
        var completion = new TaskCompletionSource<Result<DirectoryGroupReadData>>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Task<Result<DirectoryGroupReadData>> Load(CancellationToken _) { calls++; return completion.Task; }
        var first = cache.LoadAsync(Connection, "example.test", "identity:one", Load, CancellationToken.None);
        var second = cache.LoadAsync(Connection, "example.test", "identity:one", Load, CancellationToken.None);
        Assert.Null(cache.Read(Connection, "example.test", "identity:one", CancellationToken.None).Data.Identity);
        completion.SetResult(Empty());
        await Task.WhenAll(first, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ReadCapIncludesFailureOnlyEntriesAndCancellationStoresNoFacts()
    {
        using var cache = Create(1);
        await cache.LoadAsync(Connection, "example.test", "identity:one", _ => Task.FromResult(Empty()), CancellationToken.None);
        await cache.LoadAsync(Connection, "example.test", "identity:two", _ => Task.FromResult(Result.Failure<DirectoryGroupReadData>(new(ErrorCode.AccessDenied, "Denied"))), CancellationToken.None);
        Assert.Null(cache.Read(Connection, "example.test", "identity:one", CancellationToken.None).Data.Identity);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.LoadAsync(Connection, "example.test", "identity:three", async _ =>
        {
            await cancellation.CancelAsync(); return Empty();
        }, cancellation.Token));
        Assert.Null(cache.Read(Connection, "example.test", "identity:three", CancellationToken.None).Data.Identity);
    }
}
