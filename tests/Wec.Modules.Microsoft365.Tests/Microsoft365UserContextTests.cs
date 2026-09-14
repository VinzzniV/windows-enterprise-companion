using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Microsoft365;
using Wec.Core.Results;
using Wec.Modules.Microsoft365.Application;

namespace Wec.Modules.Microsoft365.Tests;

public sealed class Microsoft365UserContextTests
{
    private const string TenantId = "11111111-1111-1111-1111-111111111111";
    private const string UserId = "22222222-2222-2222-2222-222222222222";
    private const string OtherId = "33333333-3333-3333-3333-333333333333";
    private const string Sid = "S-1-5-21-1-2-3-1001";
    private readonly IMicrosoft365Reader _reader = Substitute.For<IMicrosoft365Reader>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public Microsoft365UserContextTests()
    {
        _reader.Connection.Returns(new Microsoft365Connection(new(TenantId, OtherId, EnableIntune: true), true, "Account", []));
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _reader.ReadAsync(Arg.Any<Microsoft365Query>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new Microsoft365Data()));
    }

    private Microsoft365Service Create() => new(_reader, _clock, Options.Create(new Microsoft365CacheOptions()));
    private static Microsoft365User User(string id = UserId) => new(id, "Name", "same@example.test", null, null, null, null, null, null, null, Sid, null, null);

    [Fact]
    public async Task EmptyUserContextDoesNotReadSourcesOrClaimNoLicenses()
    {
        using var service = Create();
        var context = (await service.ReadCachedAsync(TenantId, UserId, Sid, CancellationToken.None)).Value;
        Assert.All(context.UserReads, read => Assert.Equal(Microsoft365Availability.NotCached, read.State.Availability));
        Assert.Null(context.UserLicenses!.State.LoadedCount);
        Assert.Equal(Microsoft365Availability.NotEnabled, context.SignIn!.State.Availability);
        Assert.Equal(Microsoft365Availability.NotEnabled, context.Registration!.State.Availability);
        await _reader.DidNotReceive().ReadAsync(Arg.Any<Microsoft365Query>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExactSidQueryRetainsDuplicatesAndDoesNotReplaceInventoryCoverage()
    {
        using var service = Create();
        var sidQuery = new Microsoft365Query(Microsoft365Resource.UsersBySid, SecurityIdentifier: Sid);
        _reader.ReadAsync(sidQuery, Arg.Any<CancellationToken>()).Returns(Result.Success(new Microsoft365Data { Users = [User(), User(OtherId)], Truncated = true }));
        await service.ReadAsync(sidQuery, true, CancellationToken.None, TenantId);
        var context = (await service.ReadCachedAsync(TenantId, null, Sid, CancellationToken.None)).Value;
        var exact = context.UserReads.Single(read => read.State.Query.Resource == Microsoft365Resource.UsersBySid);
        Assert.Equal(2, exact.Users.Count);
        Assert.Equal(EvidenceCoverage.Partial, exact.State.Coverage);
        Assert.Equal(Microsoft365Availability.NotCached, context.UserReads[0].State.Availability);
        Assert.Empty((await service.StatusAsync(CancellationToken.None)).Sources);
    }

    [Fact]
    public async Task LicensesAndGroupsKeepIndependentFailureAndCoverage()
    {
        using var service = Create();
        var user = new Microsoft365Query(Microsoft365Resource.User, UserId);
        var licenses = new Microsoft365Query(Microsoft365Resource.UserLicenses, UserId);
        var groups = new Microsoft365Query(Microsoft365Resource.UserGroups, UserId);
        _reader.ReadAsync(user, Arg.Any<CancellationToken>()).Returns(Result.Success(new Microsoft365Data { Users = [User()] }));
        _reader.ReadAsync(licenses, Arg.Any<CancellationToken>()).Returns(Result.Failure<Microsoft365Data>(new(ErrorCode.Microsoft365AccessDenied, "Denied")));
        _reader.ReadAsync(groups, Arg.Any<CancellationToken>()).Returns(Result.Success(new Microsoft365Data { Groups = [], Truncated = true, TotalCount = 20 }));
        await service.ReadAsync(user, true, CancellationToken.None);
        await service.ReadAsync(licenses, true, CancellationToken.None);
        await service.ReadAsync(groups, true, CancellationToken.None);
        var context = (await service.ReadCachedAsync(TenantId, UserId, null, CancellationToken.None)).Value;
        Assert.Single(context.UserReads.Single(read => read.State.Query == user).Users);
        Assert.Equal(Microsoft365Availability.Unavailable, context.UserLicenses!.State.Availability);
        Assert.Null(context.UserLicenses.State.LoadedCount);
        Assert.Equal(EvidenceCoverage.Partial, context.DirectGroups!.State.Coverage);
        Assert.Equal(20, context.DirectGroups.State.DeclaredTotal);
    }

    [Fact]
    public async Task IntuneAssociationsUseUserIdAndNeverUpn()
    {
        using var service = Create();
        Microsoft365ManagedDevice managed = new(OtherId, "Device", UserId, "same@example.test", null, null, null, null, null, null, null, null, null, null);
        _reader.ReadAsync(new(Microsoft365Resource.ManagedDevices), Arg.Any<CancellationToken>()).Returns(Result.Success(new Microsoft365Data
        {
            ManagedDevices = [managed, managed with { Id = TenantId, UserId = OtherId }, managed with { Id = UserId, UserId = null }],
        }));
        await service.ReadAsync(new(Microsoft365Resource.ManagedDevices), true, CancellationToken.None);
        var context = (await service.ReadCachedAsync(TenantId, UserId, null, CancellationToken.None)).Value;
        Assert.Equal(OtherId, Assert.Single(context.AssociatedIntune[0].Devices).Id);
        Assert.Equal(3, context.AssociatedIntune[0].State.LoadedCount);
    }

    [Fact]
    public async Task UserContextRejectsAnotherTenantAndExpiresWithoutRemoteRefresh()
    {
        using var service = Create();
        _reader.ReadAsync(new(Microsoft365Resource.User, UserId), Arg.Any<CancellationToken>()).Returns(Result.Success(new Microsoft365Data { Users = [User()] }));
        await service.ReadAsync(new(Microsoft365Resource.User, UserId), false, CancellationToken.None);
        Assert.True((await service.ReadCachedAsync(OtherId, UserId, null, CancellationToken.None)).IsFailure);
        _clock.UtcNow.Returns(_clock.UtcNow.AddHours(2));
        var context = (await service.ReadCachedAsync(TenantId, UserId, null, CancellationToken.None)).Value;
        Assert.All(context.UserReads, read => Assert.Empty(read.Users));
        await _reader.Received(1).ReadAsync(Arg.Any<Microsoft365Query>(), Arg.Any<CancellationToken>());
    }
}
