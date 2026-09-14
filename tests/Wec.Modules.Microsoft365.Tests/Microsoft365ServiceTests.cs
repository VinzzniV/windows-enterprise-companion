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
        _reader.Connection.Returns(new Microsoft365Connection(new("11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222"), true, "Account", []));
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
    public async Task CachedViewsAndStatusDoNotWaitForUnrelatedRemoteRead()
    {
        using var service = Create();
        await service.ReadAsync(Users, false, TestContext());
        var completion = new TaskCompletionSource<Result<Microsoft365Data>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var groups = new Microsoft365Query(Microsoft365Resource.Groups);
        _reader.ReadAsync(groups, Arg.Any<CancellationToken>()).Returns(completion.Task);
        Task<Result<Wec.Modules.Microsoft365.Domain.Microsoft365Snapshot>> reading = service.ReadAsync(groups, false, TestContext());
        try
        {
            var status = service.StatusAsync(TestContext());
            var context = service.ContextAsync(null, null, null, null, TestContext());
            var cached = service.ReadAsync(Users, false, TestContext());
            Assert.True(status.IsCompletedSuccessfully);
            Assert.True(context.IsCompletedSuccessfully);
            Assert.True(cached.IsCompletedSuccessfully);
            Assert.True((await status).Queries.Single(state => state.Query == groups).Loading);
            Assert.False((await status).Queries.Single(state => state.Query == Users).Loading);
        }
        finally { completion.SetResult(Result.Success(new Microsoft365Data())); await reading; }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentRefreshesCoalesceWithoutClockAdvancing(bool success)
    {
        using var service = Create();
        var completion = new TaskCompletionSource<Result<Microsoft365Data>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _reader.ReadAsync(Users, Arg.Any<CancellationToken>()).Returns(completion.Task);
        var first = service.ReadAsync(Users, true, TestContext());
        var second = service.ReadAsync(Users, true, TestContext());
        completion.SetResult(success ? Result.Success(new Microsoft365Data())
            : Result.Failure<Microsoft365Data>(new(ErrorCode.Microsoft365Throttled, "Retry later")));
        var results = await Task.WhenAll(first, second);
        Assert.All(results, result => Assert.Equal(success, result.IsSuccess));
        await _reader.Received(1).ReadAsync(Users, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SourceTimesCoverageAndRefreshFailuresRemainIndependent()
    {
        using var service = Create();
        var devices = new Microsoft365Query(Microsoft365Resource.Devices);
        _reader.ReadAsync(devices, Arg.Any<CancellationToken>()).Returns(Result.Success(new Microsoft365Data { Truncated = true, TotalCount = 100 }));
        var first = await service.ReadAsync(devices, false, TestContext());
        _clock.UtcNow.Returns(_clock.UtcNow.AddMinutes(11));
        await service.ReadAsync(Users, false, TestContext());
        _reader.ReadAsync(devices, Arg.Any<CancellationToken>()).Returns(Result.Failure<Microsoft365Data>(new(ErrorCode.Microsoft365Offline, "Offline")));
        await service.ReadAsync(devices, true, TestContext());
        var status = await service.StatusAsync(TestContext());
        var deviceState = status.Queries.Single(state => state.Query == devices);
        var userState = status.Queries.Single(state => state.Query == Users);
        Assert.Equal(first.Value.UpdatedAtUtc, deviceState.RetrievedAtUtc);
        Assert.Equal(_clock.UtcNow, deviceState.LastAttemptAtUtc);
        Assert.Equal(EvidenceFreshness.Stale, deviceState.Freshness);
        Assert.Equal(EvidenceCoverage.Partial, deviceState.Coverage);
        Assert.Equal(100, deviceState.DeclaredTotal);
        Assert.Equal(0, deviceState.LoadedCount);
        Assert.Equal(ErrorCode.Microsoft365Offline, deviceState.LastAttemptError!.Code);
        Assert.Equal(EvidenceFreshness.Fresh, userState.Freshness);
        Assert.Null(userState.LastAttemptError);
        Assert.True(deviceState.SnapshotRevision > first.Value.State!.SnapshotRevision);
    }

    [Fact]
    public async Task FutureSourceTimeHasUnknownFreshness()
    {
        using var service = Create();
        await service.ReadAsync(Users, false, TestContext());
        _clock.UtcNow.Returns(_clock.UtcNow.AddMinutes(-1));
        var cached = await service.ReadAsync(Users, false, TestContext());
        Assert.Equal(EvidenceFreshness.Unknown, cached.Value.State!.Freshness);
        Assert.True(cached.Value.Stale);
    }

    [Fact]
    public async Task FailedDetailReadsAreBoundedAndExpire()
    {
        using var service = Create(2);
        _reader.ReadAsync(Arg.Any<Microsoft365Query>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Microsoft365Data>(new(ErrorCode.Microsoft365AccessDenied, "Missing role")));
        for (int index = 0; index < 3; index++)
        {
            await service.ReadAsync(new(Microsoft365Resource.User, Guid.NewGuid().ToString("D")), true, TestContext());
        }
        Assert.Equal(2, (await service.StatusAsync(TestContext())).Queries.Count);
        _clock.UtcNow.Returns(_clock.UtcNow.AddHours(2));
        Assert.Empty((await service.StatusAsync(TestContext())).Queries);
    }

    [Fact]
    public async Task FailedRefreshDoesNotExtendFactRetention()
    {
        using var service = Create();
        await service.ReadAsync(Users, false, TestContext());
        _clock.UtcNow.Returns(_clock.UtcNow.AddMinutes(59));
        _reader.ReadAsync(Users, Arg.Any<CancellationToken>()).Returns(Result.Failure<Microsoft365Data>(new(ErrorCode.Microsoft365Offline, "Offline")));
        await service.ReadAsync(Users, true, TestContext());
        _clock.UtcNow.Returns(_clock.UtcNow.AddMinutes(2));
        var state = Assert.Single((await service.StatusAsync(TestContext())).Queries);
        Assert.Null(state.RetrievedAtUtc);
        Assert.Null(state.LoadedCount);
        Assert.Equal(Microsoft365Availability.Unavailable, state.Availability);
        Assert.Equal(ErrorCode.Microsoft365Offline, state.LastAttemptError!.Code);
        Assert.True((await service.ReadAsync(Users, false, TestContext())).IsFailure);
    }

    [Fact]
    public async Task TenantSwitchDiscardsLateFailureFromPreviousSession()
    {
        using var service = Create();
        var completion = new TaskCompletionSource<Result<Microsoft365Data>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _reader.ReadAsync(Users, Arg.Any<CancellationToken>()).Returns(completion.Task);
        var reading = service.ReadAsync(Users, true, TestContext());
        var newConfiguration = new Microsoft365Configuration("33333333-3333-3333-3333-333333333333", "22222222-2222-2222-2222-222222222222");
        _reader.ConnectAsync(newConfiguration, Arg.Any<CancellationToken>()).Returns(call =>
        {
            var connection = new Microsoft365Connection(newConfiguration, true, "New account", []);
            _reader.Connection.Returns(connection);
            return Result.Success(connection);
        });
        var connecting = service.ConnectAsync(newConfiguration, TestContext());
        var changing = await service.StatusAsync(TestContext());
        Assert.False(changing.Connection.Connected);
        Assert.Empty(changing.Queries);
        completion.SetResult(Result.Failure<Microsoft365Data>(new(ErrorCode.Microsoft365AccessDenied, "Old session failure")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        await connecting;
        var connected = await service.StatusAsync(TestContext());
        Assert.Empty(connected.Queries);
        Assert.True(connected.SessionRevision > 0);
        Assert.Equal(newConfiguration.TenantId, connected.Connection.Configuration.TenantId);
    }

    [Fact]
    public async Task CancelledTenantSwitchDoesNotExposeOldConnection()
    {
        using var service = Create();
        var completion = new TaskCompletionSource<Result<Microsoft365Data>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _reader.ReadAsync(Users, Arg.Any<CancellationToken>()).Returns(completion.Task);
        var reading = service.ReadAsync(Users, true, TestContext());
        using var cancellation = new CancellationTokenSource();
        var connecting = service.ConnectAsync(_reader.Connection.Configuration, cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting);
        Assert.False((await service.StatusAsync(TestContext())).Connection.Connected);
        Assert.True((await service.ReadAsync(Users, false, TestContext())).IsFailure);
        completion.SetResult(Result.Success(new Microsoft365Data()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
        Assert.Empty((await service.StatusAsync(TestContext())).Queries);
    }

    [Fact]
    public async Task PartialUserSetCannotConfirmUniqueSidRelationship()
    {
        using var service = Create();
        const string sid = "S-1-5-21-1-2-3-1001";
        _reader.ReadAsync(Users, Arg.Any<CancellationToken>()).Returns(Result.Success(new Microsoft365Data
        {
            Users = [new(Guid.NewGuid().ToString("D"), "Name", null, null, null, null, null, null, null, null, sid, null, null)],
            Truncated = true,
        }));
        await service.ReadAsync(Users, false, TestContext());
        var context = await service.ContextAsync(sid, null, null, null, TestContext());
        Assert.Equal("Candidate", context.State);
        Assert.Contains("partial", context.Explanation, StringComparison.Ordinal);
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
    [InlineData(Microsoft365Resource.Users, "11111111-1111-1111-1111-111111111111")]
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
