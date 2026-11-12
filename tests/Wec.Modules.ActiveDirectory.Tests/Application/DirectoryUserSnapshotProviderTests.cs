using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.ActiveDirectory.Application;

namespace Wec.Modules.ActiveDirectory.Tests.Application;

public sealed class DirectoryUserSnapshotProviderTests
{
    private readonly IDirectoryUserReadProvider _reader = Substitute.For<IDirectoryUserReadProvider>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly DirectoryUserLookup _query = new(new("example.test", null, ScanCredentials.CurrentUser), "example.test", Guid.NewGuid());

    [Fact]
    public async Task CachedReadDoesNotQueryDirectoryAndExplicitReadUsesExpectedScope()
    {
        using var cache = new DirectoryUserSnapshotCache(_clock, Options.Create(new ActiveDirectoryOptions()));
        var provider = new DirectoryUserSnapshotProvider(_reader, cache, _clock);
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _reader.GetByIdAsync(Arg.Any<DirectoryUserIdentityQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Success<DirectoryUserRecord?>(null));
        Assert.Null(await provider.ReadCachedAsync(_query, CancellationToken.None));
        Assert.Empty(_reader.ReceivedCalls());
        Assert.True((await provider.ReadIdentityAsync(_query, CancellationToken.None)).IsSuccess);
        var cached = await provider.ReadCachedAsync(_query, CancellationToken.None);
        Assert.NotNull(cached?.Data);
        Assert.Null(cached.Data.User);
        await _reader.Received(1).GetByIdAsync(new(_query.Connection, _query.ObjectId!.Value, _query.DirectoryScope), CancellationToken.None);
    }

    [Theory]
    [InlineData("bad/scope", null)]
    [InlineData("example.test", "S-1-5-32-544")]
    [InlineData("example.test", "S-1-5-21-1-2-3-1000")]
    public async Task InvalidOrCompetingIdentityNeverReachesDirectory(string scope, string? sid)
    {
        using var cache = new DirectoryUserSnapshotCache(_clock, Options.Create(new ActiveDirectoryOptions()));
        var provider = new DirectoryUserSnapshotProvider(_reader, cache, _clock);
        Assert.True((await provider.ReadIdentityAsync(_query with { DirectoryScope = scope, SecurityIdentifier = sid }, CancellationToken.None)).IsFailure);
        Assert.Empty(_reader.ReceivedCalls());
    }
}
