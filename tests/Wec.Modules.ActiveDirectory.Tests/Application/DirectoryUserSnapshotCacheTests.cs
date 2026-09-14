using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.ActiveDirectory.Application;

namespace Wec.Modules.ActiveDirectory.Tests.Application;

public sealed class DirectoryUserSnapshotCacheTests
{
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly DirectoryUserLookup _query = new(new("example.test", null, ScanCredentials.CurrentUser),
        "example.test", Guid.NewGuid());

    public DirectoryUserSnapshotCacheTests() => _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
    private DirectoryUserSnapshotCache Create(int limit = 64) => new(_clock,
        Options.Create(new ActiveDirectoryOptions { IdentityCacheMaximumEntries = limit }));
    private Task<Result<DirectoryUserIdentityResult>> Load(CancellationToken _) =>
        Task.FromResult(Result.Success(new DirectoryUserIdentityResult("example.test", _clock.UtcNow, null)));

    [Fact]
    public async Task FailedAttemptKeepsSameSourceFactsUntilOriginalRetention()
    {
        using var cache = Create();
        await cache.LoadAsync(_query, Load, CancellationToken.None);
        var before = cache.Read(_query, CancellationToken.None)!;
        _clock.UtcNow.Returns(_clock.UtcNow.AddMinutes(59));
        await cache.LoadAsync(_query, _ => Task.FromResult(Result.Failure<DirectoryUserIdentityResult>(new(ErrorCode.AccessDenied, "Denied"))), CancellationToken.None);
        var stale = cache.Read(_query, CancellationToken.None)!;
        Assert.Same(before.Data, stale.Data);
        Assert.Equal(before.RetainedUntilUtc, stale.RetainedUntilUtc);
        Assert.Equal(_clock.UtcNow, stale.LastAttemptAtUtc);
        Assert.True(stale.Stale);
        Assert.True(stale.Revision > before.Revision);
        _clock.UtcNow.Returns(_clock.UtcNow.AddMinutes(2));
        var expired = cache.Read(_query, CancellationToken.None)!;
        Assert.Null(expired.Data);
        Assert.Equal(ErrorCode.AccessDenied, expired.LastAttemptError!.Code);
    }

    [Fact]
    public async Task CredentialChangeClearsEvidenceAndDiscardsLateCompletion()
    {
        using var cache = Create();
        await cache.LoadAsync(_query, Load, CancellationToken.None);
        var completion = new TaskCompletionSource<Result<DirectoryUserIdentityResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loading = cache.LoadAsync(_query, _ => completion.Task, CancellationToken.None);
        Assert.NotNull(cache.Read(_query, CancellationToken.None));
        var changed = _query with { Connection = _query.Connection with { Credentials = ScanCredentials.Explicit("test", "EXAMPLE", "test-only-placeholder") } };
        Assert.Null(cache.Read(changed, CancellationToken.None));
        completion.SetResult((await Load(CancellationToken.None)));
        Assert.True((await loading).IsFailure);
        Assert.Null(cache.Read(changed, CancellationToken.None));
        Assert.Null(cache.Read(_query, CancellationToken.None));
    }

    [Fact]
    public async Task SameQueryRefreshCoalescesAndCacheReadDoesNotWaitForSource()
    {
        using var cache = Create();
        var completion = new TaskCompletionSource<Result<DirectoryUserIdentityResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Task<Result<DirectoryUserIdentityResult>> Read(CancellationToken _) { calls++; return completion.Task; }
        var first = cache.LoadAsync(_query, Read, CancellationToken.None);
        var second = cache.LoadAsync(_query, Read, CancellationToken.None);
        Assert.Null(cache.Read(_query, CancellationToken.None));
        completion.SetResult(await Load(CancellationToken.None));
        await Task.WhenAll(first, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task EntryLimitAndCancelledLoadLeaveNoExtraSnapshot()
    {
        using var cache = Create(1);
        await cache.LoadAsync(_query, Load, CancellationToken.None);
        var second = _query with { ObjectId = Guid.NewGuid() };
        await cache.LoadAsync(second, Load, CancellationToken.None);
        Assert.Null(cache.Read(_query, CancellationToken.None));
        Assert.NotNull(cache.Read(second, CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.LoadAsync(_query, async _ =>
        {
            await cancellation.CancelAsync();
            return await Load(CancellationToken.None);
        }, cancellation.Token));
        Assert.Null(cache.Read(_query, CancellationToken.None));
    }
}
