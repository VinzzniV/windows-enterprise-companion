using System.Security.Principal;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Objects;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.ActiveDirectory.Application;

namespace Wec.Modules.ActiveDirectory.Tests.Application;

public sealed class DirectoryGroupReadServiceTests
{
    private static readonly Guid GroupId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private readonly IDirectoryReader _reader = Substitute.For<IDirectoryReader>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private static DirectoryUserReadConnection Connection => new("example.test", "dc.example.test", ScanCredentials.CurrentUser);
    private static DirectoryGroupIdentityQuery Query => new(Connection, "example.test", GroupId);

    public DirectoryGroupReadServiceTests()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _reader.SearchAsync(Arg.Is<DirectorySearchQuery>(query => query.Scope == DirectorySearchScope.Base), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DirectoryEntryData>>([new("", new Dictionary<string, IReadOnlyList<string>> { ["defaultNamingContext"] = ["DC=example,DC=test"] })]));
        _reader.SearchBoundedAsync(Arg.Any<DirectorySearchQuery>(), 2, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new BoundedDirectorySearchResult(1, [Entry()])));
        _reader.SearchPageAsync(Arg.Any<DirectorySearchQuery>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new BoundedDirectorySearchResult(0, [])));
    }
    private DirectoryGroupReadService Create()
    {
        var options = Options.Create(new ActiveDirectoryOptions());
        return new(new DomainContextService(Substitute.For<IWmiQueryService>(), _reader, options), _reader, _clock, options);
    }
    private static DirectoryEntryData Entry(string name = "Operations", string dn = "CN=Operations,DC=example,DC=test",
        string? type = "-2147483646", string[]? classes = null, bool includeId = true)
    {
        var sid = new SecurityIdentifier("S-1-5-32-544");
        byte[] bytes = new byte[sid.BinaryLength]; sid.GetBinaryForm(bytes, 0);
        var attributes = new Dictionary<string, IReadOnlyList<string>>
        {
            ["name"] = [name], ["objectSid"] = [Convert.ToBase64String(bytes)], ["objectClass"] = classes ?? ["top", "group"],
            ["groupType"] = type is null ? [] : [type], ["userPrincipalName"] = ["unverified@example.test"],
        };
        if (includeId) { attributes["objectGUID"] = [Convert.ToBase64String(GroupId.ToByteArray())]; }
        return new(dn, attributes);
    }

    [Fact]
    public async Task GuidLookupUsesOnlyAllowlistedAttributesAndPreservesScopedNativeIdentity()
    {
        var result = await Create().ReadIdentityAsync(Query, CancellationToken.None);
        var group = Assert.Single(result.Value.Groups);
        Assert.Equal(GroupId, group.ObjectId);
        Assert.Equal("S-1-5-32-544", group.SecurityIdentifier);
        Assert.Equal("example.test", group.DirectoryScope);
        Assert.True(group.SecurityEnabled);
        Assert.Equal("Global", group.GroupScope);
        await _reader.Received(1).SearchBoundedAsync(Arg.Is<DirectorySearchQuery>(query =>
            query.LdapFilter == @"(&(objectCategory=group)(objectGUID=\33\22\11\00\55\44\77\66\88\99\aa\bb\cc\dd\ee\ff))"
            && !query.Attributes.Contains("member") && query.Server == "dc.example.test"), 2, CancellationToken.None);
    }

    [Fact]
    public async Task BuiltinGroupSidCanBeReadWithoutUsingLocalizedNames()
    {
        var result = await Create().ReadIdentityAsync(Query with { ObjectId = null, SecurityIdentifier = "S-1-5-32-544" }, CancellationToken.None);
        Assert.True(result.IsSuccess);
        await _reader.Received(1).SearchBoundedAsync(Arg.Is<DirectorySearchQuery>(query => query.LdapFilter == "(&(objectCategory=group)(objectSid=S-1-5-32-544))"), 2, CancellationToken.None);
    }

    [Fact]
    public async Task ExactDnFilterEscapesLdapOperatorsAndPreservesTheOriginalName()
    {
        const string dn = @"CN=Operations*(West)\, Team,DC=example,DC=test";
        _reader.SearchBoundedAsync(Arg.Any<DirectorySearchQuery>(), 2, Arg.Any<CancellationToken>()).Returns(Result.Success(new BoundedDirectorySearchResult(1, [Entry(dn: dn)])));
        var result = await Create().ReadIdentityAsync(Query with { ObjectId = null, DistinguishedName = dn }, CancellationToken.None);
        Assert.Equal(dn, Assert.Single(result.Value.Groups).DistinguishedName);
        await _reader.Received(1).SearchBoundedAsync(Arg.Is<DirectorySearchQuery>(query => query.LdapFilter == @"(&(objectCategory=group)(distinguishedName=CN=Operations\2a\28West\29\5c, Team,DC=example,DC=test))"), 2, CancellationToken.None);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task InvalidPagesNeverReachDirectory(int page, int size)
    {
        Assert.True((await Create().ReadPageAsync(new(Connection, "example.test", Page: page, PageSize: size), CancellationToken.None)).IsFailure);
        Assert.Empty(_reader.ReceivedCalls());
    }

    [Fact]
    public async Task GroupPageUsesStableServerSortingAndPreservesDuplicateNamesAndMissingIds()
    {
        _reader.SearchPageAsync(Arg.Any<DirectorySearchQuery>(), 50, 25, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new BoundedDirectorySearchResult(80, [Entry(), Entry(dn: "CN=Operations,OU=Other,DC=example,DC=test", includeId: false)])));
        var page = (await Create().ReadPageAsync(new(Connection, "example.test", "Operations", 3, 25), CancellationToken.None)).Value;
        Assert.Equal(80, page.TotalCount);
        Assert.Equal(2, page.Groups.Count);
        Assert.Null(page.Groups[1].ObjectId);
        await _reader.Received(1).SearchPageAsync(Arg.Is<DirectorySearchQuery>(query => query.SortAttribute == "name"
            && query.SortTieBreakerAttribute == "sAMAccountName"), 50, 25, CancellationToken.None);
    }

    [Fact]
    public async Task DirectMemberReadRetainsComputersNestedGroupsAndLimitedObjectsWithoutRecursion()
    {
        _reader.SearchPageAsync(Arg.Any<DirectorySearchQuery>(), 0, 50, Arg.Any<CancellationToken>()).Returns(Result.Success(new BoundedDirectorySearchResult(3,
            [Entry(classes: ["top", "person", "user", "computer"]), Entry(), Entry(classes: ["top", "foreignSecurityPrincipal"], includeId: false)])));
        var page = (await Create().ReadMembersAsync(new(Connection, "example.test", GroupId), CancellationToken.None)).Value;
        Assert.Equal(ObjectKind.Device, page.Members[0].Kind);
        Assert.Null(page.Members[0].UserPrincipalName);
        Assert.Equal(ObjectKind.Group, page.Members[1].Kind);
        Assert.Null(page.Members[2].Kind);
        Assert.Null(page.Members[2].ObjectId);
        Assert.Contains("Primary-group membership", page.CoverageExplanation);
        Assert.Contains("effective permissions are not evaluated", page.CoverageExplanation);
        await _reader.Received(1).SearchPageAsync(Arg.Is<DirectorySearchQuery>(query => query.LdapFilter == "(&(objectClass=*)(memberOf=CN=Operations,DC=example,DC=test))"
            && !query.LdapFilter.Contains("1.2.840.113556.1.4.1941")), 0, 50, CancellationToken.None);
    }

    [Fact]
    public async Task AmbiguousGroupPreventsMemberLookup()
    {
        _reader.SearchBoundedAsync(Arg.Any<DirectorySearchQuery>(), 2, Arg.Any<CancellationToken>()).Returns(Result.Success(new BoundedDirectorySearchResult(2, [Entry(), Entry()])));
        Assert.Equal(2, (await Create().ReadIdentityAsync(Query, CancellationToken.None)).Value.Groups.Count);
        Assert.True((await Create().ReadMembersAsync(new(Connection, "example.test", GroupId), CancellationToken.None)).IsFailure);
        await _reader.DidNotReceiveWithAnyArgs().SearchPageAsync(default!, default, default, default);
    }

    [Fact]
    public async Task WrongActualScopePreventsObjectReads()
    {
        Assert.True((await Create().ReadIdentityAsync(Query with { DirectoryScope = "other.test" }, CancellationToken.None)).IsFailure);
        await _reader.DidNotReceiveWithAnyArgs().SearchBoundedAsync(default!, default, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown")]
    [InlineData("4294967296")]
    public void MissingOrInvalidGroupTypeRemainsUnknown(string? value)
    {
        var group = DirectoryGroupReadService.MapGroup(Entry(type: value), "example.test");
        Assert.Null(group.SecurityEnabled);
        Assert.Null(group.GroupScope);
    }

    [Fact]
    public async Task MissingGuidCannotSatisfyGuidIdentityAndCancellationIsPreserved()
    {
        _reader.SearchBoundedAsync(Arg.Any<DirectorySearchQuery>(), 2, Arg.Any<CancellationToken>()).Returns(Result.Success(new BoundedDirectorySearchResult(1, [Entry(includeId: false)])));
        Assert.True((await Create().ReadIdentityAsync(Query, CancellationToken.None)).IsFailure);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().ReadIdentityAsync(Query, cancellation.Token));
    }
}
