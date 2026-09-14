using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Microsoft365;
using Wec.Core.Results;
using Wec.Modules.Microsoft365.Application;

namespace Wec.Modules.Microsoft365.Tests;

public sealed class Microsoft365GroupContextTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string GroupId = "22222222-2222-2222-2222-222222222222";
    private readonly IMicrosoft365Reader _reader = Substitute.For<IMicrosoft365Reader>();
    private readonly IClock _clock = Substitute.For<IClock>();
    public Microsoft365GroupContextTests()
    {
        _reader.Connection.Returns(new Microsoft365Connection(new(Tenant, GroupId), true, "Account", []));
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
    }
    private Microsoft365Service Create() => new(_reader, _clock, Options.Create(new Microsoft365CacheOptions()));

    [Fact]
    public async Task CacheOnlyGroupReadStartsNoSourceRequestAndReportsUnknownMembers()
    {
        using var service = Create();
        var context = (await service.ReadGroupCachedAsync(Tenant, GroupId, CancellationToken.None)).Value;
        Assert.Equal(Microsoft365Availability.NotCached, context.DirectMembers!.State.Availability);
        Assert.Null(context.DirectMembers.State.LoadedCount);
        Assert.All(context.GroupReads, read => Assert.Empty(read.Groups));
        await _reader.DidNotReceiveWithAnyArgs().ReadAsync(default!, default);
    }

    [Fact]
    public async Task DuplicateGroupsAndLimitedMembersRetainOriginalQueryCoverage()
    {
        using var service = Create();
        var group = new Microsoft365Group(GroupId, "Name", null, null, null, null, null, null);
        _reader.ReadAsync(Arg.Any<Microsoft365Query>(), Arg.Any<CancellationToken>()).Returns(call => Result.Success(
            call.Arg<Microsoft365Query>().Resource == Microsoft365Resource.Groups ? new Microsoft365Data { Groups = [group, group], Truncated = true }
                : new Microsoft365Data { Members = [new(GroupId, null, "user", null), new(null, "Unknown object", null, null)], Truncated = true }));
        await service.ReadAsync(new(Microsoft365Resource.Groups), true, CancellationToken.None);
        await service.ReadAsync(new(Microsoft365Resource.GroupMembers, GroupId), true, CancellationToken.None);
        var context = (await service.ReadGroupCachedAsync(Tenant, GroupId, CancellationToken.None)).Value;
        Assert.Equal(2, context.GroupReads[0].Groups.Count);
        Assert.Equal(EvidenceCoverage.Partial, context.DirectMembers!.State.Coverage);
        Assert.Null(context.DirectMembers.Members[0].DisplayName);
        Assert.Null(context.DirectMembers.Members[1].Id);
    }

    [Fact]
    public async Task MemberFailureDoesNotEraseGroupAndWrongTenantCannotReadEither()
    {
        using var service = Create();
        _reader.ReadAsync(Arg.Any<Microsoft365Query>(), Arg.Any<CancellationToken>()).Returns(call =>
            call.Arg<Microsoft365Query>().Resource == Microsoft365Resource.GroupMembers
                ? Result.Failure<Microsoft365Data>(new(ErrorCode.Microsoft365AccessDenied, "Denied"))
                : Result.Success(new Microsoft365Data { Groups = [new(GroupId, "Group", true, false, [], null, null, null)] }));
        await service.ReadAsync(new(Microsoft365Resource.Group, GroupId), true, CancellationToken.None);
        await service.ReadAsync(new(Microsoft365Resource.GroupMembers, GroupId), true, CancellationToken.None);
        var context = (await service.ReadGroupCachedAsync(Tenant, GroupId, CancellationToken.None)).Value;
        Assert.Single(context.GroupReads.Single(read => read.State.Query.Resource == Microsoft365Resource.Group).Groups);
        Assert.Equal(ErrorCode.Microsoft365AccessDenied, context.DirectMembers!.State.LastAttemptError!.Code);
        Assert.True((await service.ReadGroupCachedAsync(GroupId, GroupId, CancellationToken.None)).IsFailure);
    }
}
