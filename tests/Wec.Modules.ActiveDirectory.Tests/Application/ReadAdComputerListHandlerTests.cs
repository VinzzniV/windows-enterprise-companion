using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.ActiveDirectory.Application;
using Wec.Modules.ActiveDirectory.Handlers;

namespace Wec.Modules.ActiveDirectory.Tests.Application;

public sealed class ReadAdComputerListHandlerTests : IDisposable
{
    private readonly IDirectoryReader _reader = Substitute.For<IDirectoryReader>();
    private readonly IWmiQueryService _wmi = Substitute.For<IWmiQueryService>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly DirectoryComputerSnapshotCache _cache;
    private readonly ReadAdComputerListHandler _handler;
    private static readonly Guid ObjectId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly ReadAdComputerListRequest Request = new("example.test", new("example.test", "dc.example.test"), "PC", 1, true);

    public ReadAdComputerListHandlerTests()
    {
        var options = Options.Create(new ActiveDirectoryOptions());
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _cache = new(_clock, options);
        _handler = new(new(new(_wmi, _reader, options), _reader, options), _cache, _clock, options);
        _reader.SearchAsync(Arg.Any<DirectorySearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DirectoryEntryData>>([new("", new Dictionary<string, IReadOnlyList<string>>
            { ["defaultNamingContext"] = ["DC=example,DC=test"] })]));
        _reader.SearchBoundedAsync(Arg.Any<DirectorySearchQuery>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new BoundedDirectorySearchResult(2, [new("CN=PC,DC=example,DC=test", new Dictionary<string, IReadOnlyList<string>>
            { ["name"] = ["PC"], ["objectGUID"] = [Convert.ToBase64String(ObjectId.ToByteArray())] })])));
    }

    [Fact]
    public async Task SearchIsExplicitBoundedAndReusableByNativeProfileWithOriginalCoverage()
    {
        Assert.Null((await _handler.HandleAsync(Request with { Refresh = false }, CancellationToken.None)).Value.Read);
        Assert.Empty(_reader.ReceivedCalls());
        var loaded = (await _handler.HandleAsync(Request, CancellationToken.None)).Value.Read!;
        Assert.True(loaded.Data!.Truncated);
        Assert.Null(Assert.Single(loaded.Data.Computers).Enabled);
        await _reader.Received(1).SearchBoundedAsync(Arg.Is<DirectorySearchQuery>(query => query.Server == "dc.example.test"
            && query.Attributes.Contains("objectGUID")), 1, Arg.Any<CancellationToken>());
        var profile = _cache.Read(new(new("example.test", "dc.example.test", ScanCredentials.CurrentUser), "example.test", ObjectId), CancellationToken.None)!;
        Assert.Equal(ObjectId, Assert.Single(profile.Data!.Computers).ObjectId);
        Assert.Equal(loaded.Data.RetrievedAtUtc, profile.Data.RetrievedAtUtc);
        Assert.Equal(loaded.RetainedUntilUtc, profile.RetainedUntilUtc);
        Assert.True(profile.Data.Truncated);
        _clock.UtcNow.Returns(_clock.UtcNow.AddHours(2));
        Assert.Null((await _handler.HandleAsync(Request with { Refresh = false }, CancellationToken.None)).Value.Read);
        await _reader.ReceivedWithAnyArgs(1).SearchBoundedAsync(default!, default, default);
    }

    [Fact]
    public async Task ActualNamingContextMustMatchBeforeSubtreeSearch()
    {
        var result = await _handler.HandleAsync(Request with { DirectoryScope = "other.test" }, CancellationToken.None);
        Assert.Equal(ErrorCode.DirectoryUnavailable, result.Value.Read!.LastAttemptError!.Code);
        await _reader.DidNotReceiveWithAnyArgs().SearchBoundedAsync(default!, default, default);
    }

    [Fact]
    public async Task CredentialChangeRejectsLateResultWithoutReactivatingPreviousContext()
    {
        var completion = new TaskCompletionSource<Result<BoundedDirectorySearchResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _reader.SearchBoundedAsync(Arg.Any<DirectorySearchQuery>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(completion.Task);
        var pending = _handler.HandleAsync(Request, CancellationToken.None);
        await _handler.HandleAsync(Request with { Connection = new("example.test", "other-dc.example.test"), Refresh = false }, CancellationToken.None);
        completion.SetResult(Result.Success(new BoundedDirectorySearchResult(0, [])));
        Assert.True((await pending).IsFailure);
        Assert.Null((await _handler.HandleAsync(Request with { Refresh = false }, CancellationToken.None)).Value.Read);
    }

    [Theory]
    [InlineData(0, "PC")]
    [InlineData(101, "PC")]
    [InlineData(1, "PC\n")]
    public async Task InvalidBoundsDoNotReadDirectory(int limit, string search)
    {
        var result = await _handler.HandleAsync(Request with { Limit = limit, Search = search + "X" }, CancellationToken.None);
        Assert.Equal(ErrorCode.InvalidRequest, result.Error!.Code);
        Assert.Empty(_reader.ReceivedCalls());
    }

    public void Dispose() => _cache.Dispose();
}
