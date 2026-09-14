using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Microsoft365;
using Wec.Core.Results;
using Wec.Modules.Microsoft365.Application;

namespace Wec.Modules.Microsoft365.Tests;

public sealed class Microsoft365ServiceTests
{
    private readonly IMicrosoft365Reader _reader = Substitute.For<IMicrosoft365Reader>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private static readonly Microsoft365Query Users = new(Microsoft365Resource.Users);

    public Microsoft365ServiceTests()
    {
        _clock.UtcNow.Returns(DateTimeOffset.Parse("2026-09-14T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        _reader.ReadAsync(Arg.Any<Microsoft365Query>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new Microsoft365Data()));
    }

    private Microsoft365Service Create(int maximumEntries = 32) => new(_reader, _clock,
        Options.Create(new Microsoft365CacheOptions { MaximumEntries = maximumEntries }));

    [Fact]
    public async Task NavigationReusesStaleDataUntilExplicitRefresh()
    {
        using var service = Create();
        await service.ReadAsync(Users, false, TestContext());
        _clock.UtcNow.Returns(_clock.UtcNow.AddMinutes(11));
        var cached = await service.ReadAsync(Users, false, TestContext());
        Assert.True(cached.Value.Stale);
        await _reader.Received(1).ReadAsync(Users, Arg.Any<CancellationToken>());
        var refreshed = await service.ReadAsync(Users, true, TestContext());
        Assert.False(refreshed.Value.Stale);
        await _reader.Received(2).ReadAsync(Users, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailedRefreshPreservesPreviousTimestampAndData()
    {
        using var service = Create();
        var first = await service.ReadAsync(Users, false, TestContext());
        _clock.UtcNow.Returns(_clock.UtcNow.AddMinutes(1));
        _reader.ReadAsync(Users, Arg.Any<CancellationToken>()).Returns(Result.Failure<Microsoft365Data>(
            new Error(ErrorCode.Microsoft365Throttled, "Retry later")));
        var second = await service.ReadAsync(Users, true, TestContext());
        Assert.Equal(first.Value.Data, second.Value.Data);
        Assert.Equal(first.Value.UpdatedAtUtc, second.Value.UpdatedAtUtc);
        Assert.True(second.Value.Stale);
        Assert.Equal(ErrorCode.Microsoft365Throttled, second.Value.RefreshError!.Code);
    }

    [Fact]
    public async Task FailedFirstReadRemainsFailureRatherThanEmptySuccess()
    {
        using var service = Create();
        _reader.ReadAsync(Users, Arg.Any<CancellationToken>()).Returns(Result.Failure<Microsoft365Data>(
            new Error(ErrorCode.Microsoft365AccessDenied, "Missing role")));
        Assert.True((await service.ReadAsync(Users, false, TestContext())).IsFailure);
        var status = Assert.Single((await service.StatusAsync(TestContext())).Sources);
        Assert.Null(status.UpdatedAtUtc);
        Assert.Null(status.LoadedCount);
        Assert.Equal(ErrorCode.Microsoft365AccessDenied, status.LastRefreshError!.Code);
    }

    [Fact]
    public async Task ConcurrentRefreshesShareOneSuccessfulRead()
    {
        using var service = Create();
        var completion = new TaskCompletionSource<Result<Microsoft365Data>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _reader.ReadAsync(Users, Arg.Any<CancellationToken>()).Returns(completion.Task);
        var first = service.ReadAsync(Users, true, TestContext());
        var second = service.ReadAsync(Users, true, TestContext());
        _clock.UtcNow.Returns(_clock.UtcNow.AddSeconds(1));
        completion.SetResult(Result.Success(new Microsoft365Data()));
        await Task.WhenAll(first, second);
        await _reader.Received(1).ReadAsync(Users, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetentionAndEntryLimitsEvictOldSnapshots()
    {
        using var service = Create(1);
        await service.ReadAsync(Users, false, TestContext());
        await service.ReadAsync(new(Microsoft365Resource.Groups), false, TestContext());
        await service.ReadAsync(Users, false, TestContext());
        _clock.UtcNow.Returns(_clock.UtcNow.AddHours(2));
        await service.ReadAsync(Users, false, TestContext());
        await _reader.Received(3).ReadAsync(Users, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisconnectClearsCacheAndCancelsActiveRead()
    {
        using var service = Create();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _reader.ReadAsync(Users, Arg.Any<CancellationToken>()).Returns(async call =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
            return Result.Success(new Microsoft365Data());
        });
        var reading = service.ReadAsync(Users, false, TestContext());
        await started.Task;
        await service.DisconnectAsync(TestContext());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        Assert.Empty((await service.StatusAsync(TestContext())).Sources);
    }

    [Fact]
    public async Task CallerCancellationReachesProviderWithoutPublishingCache()
    {
        using var service = Create();
        using var cancellation = new CancellationTokenSource();
        _reader.ReadAsync(Users, Arg.Any<CancellationToken>()).Returns(async call =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
            return Result.Success(new Microsoft365Data());
        });
        var reading = service.ReadAsync(Users, false, cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        Assert.Empty((await service.StatusAsync(TestContext())).Sources);
    }

    [Fact]
    public async Task ContextReadsNeverLoadGraphAndMissingDataDoesNotMeanAbsent()
    {
        using var service = Create();
        var context = await service.ContextAsync("S-1-5-21-1-2-3-1001", null, null, null, TestContext());
        Assert.Null(context.User);
        Assert.Null(context.ObservedAtUtc);
        Assert.Contains("Missing evidence", context.Explanation, StringComparison.Ordinal);
        await _reader.DidNotReceive().ReadAsync(Arg.Any<Microsoft365Query>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(Microsoft365Resource.User, null)]
    [InlineData(Microsoft365Resource.Users, "bad")]
    [InlineData((Microsoft365Resource)999, null)]
    public async Task RejectsInvalidBridgeQueries(Microsoft365Resource resource, string? id)
    {
        using var service = Create();
        Assert.True((await service.ReadAsync(new(resource, id), false, TestContext())).IsFailure);
        await _reader.DidNotReceive().ReadAsync(Arg.Any<Microsoft365Query>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(100, 90, "User", 10, true, false)]
    [InlineData(100, 120, "User", -20, true, true)]
    [InlineData(0, 0, "User", 0, false, false)]
    [InlineData(null, 0, "User", null, null, null)]
    [InlineData(100, null, "User", null, null, null)]
    [InlineData(100, 50, "Company", null, null, null)]
    [InlineData(-1, 50, "User", null, null, null)]
    public void LicenseCapacityPreservesUnknownAndOverAssignment(int? seats, int? used, string appliesTo,
        int? remaining, bool? nearlyExhausted, bool? overAssigned)
    {
        var capacity = Microsoft365Service.Capacity(new(null, "sku", null, null, appliesTo, seats, used, null), 0.9);
        Assert.Equal(remaining, capacity.RemainingSeats);
        Assert.Equal(nearlyExhausted, capacity.NearlyExhausted);
        Assert.Equal(overAssigned, capacity.OverAssigned);
    }

    private static CancellationToken TestContext() => CancellationToken.None;
}
