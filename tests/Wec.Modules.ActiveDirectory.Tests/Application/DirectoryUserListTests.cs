using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.ActiveDirectory.Application;

namespace Wec.Modules.ActiveDirectory.Tests.Application;

public sealed class DirectoryUserListTests
{
    private readonly IDirectoryUserReadProvider _reader = Substitute.For<IDirectoryUserReadProvider>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly DirectoryUserListQuery _query = new(new("EXAMPLE", null, ScanCredentials.CurrentUser), "example.test");
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public DirectoryUserListTests() => _clock.UtcNow.Returns(new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero));
    private DirectoryUserListCache Cache(int limit = 64) => new(_clock, Options.Create(new ActiveDirectoryOptions { IdentityCacheMaximumEntries = limit }));
    private DirectoryUserListProvider Provider(DirectoryUserListCache cache) => new(_reader, cache, _clock);
    private static DirectoryUserRecord User(string scope = "example.test") => new(UserId, "S-1-5-21-1-2-3-1001", "Account", "account", "account@example.test",
        null, null, "IT", null, null, "CN=Account,DC=example,DC=test", "DC=example,DC=test", null, null, null, null, null, null, null,
        [], new(DirectoryUserAccessCoverage.NotEvaluated, "Not evaluated", []), scope);
    private static DirectoryUserPage Page(params DirectoryUserRecord[] users) => new(true, "example.test", "DC=example,DC=test", 1, 100, 420, users);
    private void Respond(DirectoryUserPage page) => _reader.GetPageAsync(Arg.Any<DirectoryUserPageQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success(page));

    [Fact]
    public async Task CachedReadIsLocalAndExplicitPagePreservesNativeDuplicatesUnknownStateAndSourceTotal()
    {
        using var cache = Cache();
        var provider = Provider(cache);
        Assert.Null((await provider.ReadCachedAsync(_query, CancellationToken.None)).Value.Data);
        Assert.Empty(_reader.ReceivedCalls());
        Respond(Page(User(), User() with { DisplayName = "Conflicting record" }));
        var result = await provider.ReadPageAsync(_query, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.LastAttemptError);
        Assert.Equal(420, result.Value.Data!.TotalCount);
        Assert.Equal(2, result.Value.Data.Users.Count);
        Assert.All(result.Value.Data.Users, user => { Assert.Equal(UserId, user.ObjectId); Assert.Null(user.Enabled); });
        await _reader.Received(1).GetPageAsync(Arg.Is<DirectoryUserPageQuery>(query => query.PageSize == 100 && query.DirectoryScope == "example.test"
            && query.SortField == DirectoryUserSortField.SamAccountName), CancellationToken.None);
        await _reader.DidNotReceiveWithAnyArgs().GetByIdAsync(default!, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrongResponseScopeIsUnavailableInsteadOfAnEmptyOrMisScopedList(bool wrongUserScope)
    {
        using var cache = Cache();
        Respond(wrongUserScope ? Page(User("other.test")) : Page() with { DomainName = "other.test" });
        var result = await Provider(cache).ReadPageAsync(_query, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Data);
        Assert.Equal(ErrorCode.DirectoryUnavailable, result.Value.LastAttemptError!.Code);
    }

    [Theory]
    [InlineData("bad/scope", 1, 100)]
    [InlineData("example.test", 0, 100)]
    [InlineData("example.test", 1, 101)]
    [InlineData("example.test", int.MaxValue, 100)]
    public async Task InvalidQueryNeverReadsOrRetainsDirectoryData(string scope, int page, int size)
    {
        using var cache = Cache();
        var query = _query with { DirectoryScope = scope, Page = page, PageSize = size };
        Assert.True((await Provider(cache).ReadPageAsync(query, CancellationToken.None)).IsFailure);
        Assert.True((await Provider(cache).ReadCachedAsync(query, CancellationToken.None)).IsFailure);
        Assert.Empty(_reader.ReceivedCalls());
    }

    [Fact]
    public async Task FailedRefreshRetainsOriginalExpiryAndNewFailureAfterDataExpires()
    {
        using var cache = Cache();
        var provider = Provider(cache);
        Respond(Page(User()));
        var before = (await provider.ReadPageAsync(_query, CancellationToken.None)).Value;
        _clock.UtcNow.Returns(_clock.UtcNow.AddMinutes(59));
        _reader.GetPageAsync(Arg.Any<DirectoryUserPageQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<DirectoryUserPage>(new(ErrorCode.AccessDenied, "Denied")));
        var failed = (await provider.ReadPageAsync(_query, CancellationToken.None)).Value;
        Assert.Same(before.Data, failed.Data);
        Assert.Equal(before.RetainedUntilUtc, failed.RetainedUntilUtc);
        Assert.True(failed.Stale);
        _clock.UtcNow.Returns(_clock.UtcNow.AddMinutes(2));
        var expired = (await provider.ReadCachedAsync(_query, CancellationToken.None)).Value;
        Assert.Null(expired.Data);
        Assert.NotNull(expired.LastAttemptError);
    }

    [Fact]
    public async Task ConcurrentLoadsCoalesceAndCredentialChangesRejectLateResponsesWithoutBlockingCacheReads()
    {
        using var cache = Cache();
        var provider = Provider(cache);
        var completion = new TaskCompletionSource<Result<DirectoryUserPage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _reader.GetPageAsync(Arg.Any<DirectoryUserPageQuery>(), Arg.Any<CancellationToken>()).Returns(completion.Task);
        var first = provider.ReadPageAsync(_query, CancellationToken.None);
        var second = provider.ReadPageAsync(_query, CancellationToken.None);
        Task<Result<CachedDirectoryUserList>> cached = provider.ReadCachedAsync(_query, CancellationToken.None);
        Assert.True(cached.IsCompletedSuccessfully);
        Assert.Null((await cached).Value.Data);
        completion.SetResult(Result.Success(Page(User())));
        await Task.WhenAll(first, second);
        await _reader.Received(1).GetPageAsync(Arg.Any<DirectoryUserPageQuery>(), Arg.Any<CancellationToken>());
        completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _reader.GetPageAsync(Arg.Any<DirectoryUserPageQuery>(), Arg.Any<CancellationToken>()).Returns(completion.Task);
        Task<Result<CachedDirectoryUserList>> late = provider.ReadPageAsync(_query, CancellationToken.None);
        var changed = _query with { Connection = _query.Connection with { Credentials = ScanCredentials.Explicit("test", "EXAMPLE", "test-only-placeholder") } };
        Assert.Null((await provider.ReadCachedAsync(changed, CancellationToken.None)).Value.Data);
        completion.SetResult(Result.Success(Page(User())));
        Assert.True((await late).IsFailure);
        Assert.Null((await provider.ReadCachedAsync(_query, CancellationToken.None)).Value.Data);
    }

    [Fact]
    public async Task PageBoundAndCancellationPreventUnboundedOrLatePublication()
    {
        using var cache = Cache(1);
        var provider = Provider(cache);
        Respond(Page(User()));
        await provider.ReadPageAsync(_query, CancellationToken.None);
        await provider.ReadPageAsync(_query with { Search = "second" }, CancellationToken.None);
        Assert.Null((await provider.ReadCachedAsync(_query, CancellationToken.None)).Value.Data);
        using var cancellation = new CancellationTokenSource();
        _reader.GetPageAsync(Arg.Any<DirectoryUserPageQuery>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cancellation.Cancel();
            return Result.Success(Page(User()));
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.ReadPageAsync(_query, cancellation.Token));
        Assert.Null((await provider.ReadCachedAsync(_query, CancellationToken.None)).Value.Data);
    }
}
