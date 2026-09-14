using System.Security.Principal;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.ActiveDirectory.Application;

namespace Wec.Modules.ActiveDirectory.Tests.Application;

public sealed class DirectoryComputerReadServiceTests
{
    private const string Scope = "example.test";
    private const string Sid = "S-1-5-21-1-2-3-1001";
    private static readonly Guid ObjectId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private readonly IDirectoryReader _reader = Substitute.For<IDirectoryReader>();
    private readonly IWmiQueryService _wmi = Substitute.For<IWmiQueryService>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public DirectoryComputerReadServiceTests()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _reader.SearchAsync(Arg.Is<DirectorySearchQuery>(query => query.Scope == DirectorySearchScope.Base), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DirectoryEntryData>>([new("", new Dictionary<string, IReadOnlyList<string>>
            {
                ["defaultNamingContext"] = ["DC=example,DC=test"],
            })]));
        Entries([Computer()]);
    }

    private DirectoryComputerReadService Create()
    {
        var options = Options.Create(new ActiveDirectoryOptions());
        return new(new DomainContextService(_wmi, _reader, options), _reader, _clock, options,
            new DirectoryComputerSnapshotCache(_clock, options));
    }

    private static DirectoryComputerIdentityQuery Query(Guid? id = null, string? sid = null) => new(
        new("EXAMPLE", "dc.example.test", ScanCredentials.CurrentUser), Scope, id, sid);

    private void Entries(IReadOnlyList<DirectoryEntryData> entries, int? total = null) =>
        _reader.SearchBoundedAsync(Arg.Any<DirectorySearchQuery>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new BoundedDirectorySearchResult(total ?? entries.Count, entries)));

    private static DirectoryEntryData Computer(Guid? id = null)
    {
        var sid = new SecurityIdentifier(Sid);
        var bytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(bytes, 0);
        return new("CN=PC,DC=example,DC=test", new Dictionary<string, IReadOnlyList<string>>
        {
            ["objectGUID"] = [Convert.ToBase64String((id ?? ObjectId).ToByteArray())],
            ["objectSid"] = [Convert.ToBase64String(bytes)],
            ["name"] = ["PC"], ["dNSHostName"] = ["pc.example.test"],
        });
    }

    [Fact]
    public async Task GuidReadUsesBoundedBinaryIdentityFilterAndPreservesUnknownState()
    {
        var result = await Create().ReadIdentityAsync(Query(ObjectId), CancellationToken.None);
        var computer = Assert.Single(result.Value.Computers);
        Assert.Equal(ObjectId, computer.ObjectId);
        Assert.Equal(Sid, computer.SecurityIdentifier);
        Assert.Equal(Scope, computer.DirectoryScope);
        Assert.Equal("pc.example.test", computer.DnsHostName);
        Assert.Null(computer.Enabled);
        await _reader.Received(1).SearchBoundedAsync(Arg.Is<DirectorySearchQuery>(query =>
            query.LdapFilter == @"(&(objectCategory=computer)(objectGUID=\33\22\11\00\55\44\77\66\88\99\aa\bb\cc\dd\ee\ff))"
            && query.Server == "dc.example.test" && query.Attributes.Contains("objectSid")), 2, Arg.Any<CancellationToken>());
        await _wmi.DidNotReceiveWithAnyArgs().QueryAsync(default!, default!, default);
    }

    [Fact]
    public async Task SidReadIsExactAndNeverUsesComputerName()
    {
        var result = await Create().ReadIdentityAsync(Query(sid: Sid), CancellationToken.None);
        Assert.Single(result.Value.Computers);
        await _reader.Received(1).SearchBoundedAsync(Arg.Is<DirectorySearchQuery>(query =>
            query.LdapFilter == $"(&(objectCategory=computer)(objectSid={Sid}))"), 2, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("S-1-5-32-544")]
    [InlineData("S-1-5-21-1-2-3-1001)(objectClass=*)")]
    public async Task InvalidIdentityIsRejectedBeforeDirectoryAccess(string? sid)
    {
        Assert.True((await Create().ReadIdentityAsync(Query(sid: sid), CancellationToken.None)).IsFailure);
        await _reader.DidNotReceiveWithAnyArgs().SearchAsync(default!, default);
        await _reader.DidNotReceiveWithAnyArgs().SearchBoundedAsync(default!, default, default);
    }

    [Fact]
    public async Task WrongActualDirectoryIsRejectedBeforeIdentitySearch()
    {
        var result = await Create().ReadIdentityAsync(Query(ObjectId) with { DirectoryScope = "other.test" }, CancellationToken.None);
        Assert.Equal(ErrorCode.DirectoryUnavailable, result.Error!.Code);
        await _reader.DidNotReceiveWithAnyArgs().SearchBoundedAsync(default!, default, default);
    }

    [Fact]
    public async Task DuplicateIdentityAndTruncationRemainVisible()
    {
        Entries([Computer(), Computer()], 3);
        var result = await Create().ReadIdentityAsync(Query(ObjectId), CancellationToken.None);
        Assert.Equal(2, result.Value.Computers.Count);
        Assert.True(result.Value.Truncated);
    }

    [Fact]
    public async Task DifferentReturnedIdentityDoesNotBecomeAProfile()
    {
        Entries([Computer(Guid.NewGuid())]);
        Assert.True((await Create().ReadIdentityAsync(Query(ObjectId), CancellationToken.None)).IsFailure);
    }

    [Fact]
    public async Task EmptyExactReadRemainsAnEmptySourceResult()
    {
        Entries([]);
        var result = await Create().ReadIdentityAsync(Query(ObjectId), CancellationToken.None);
        Assert.Empty(result.Value.Computers);
        Assert.Equal(Scope, result.Value.DirectoryScope);
        Assert.False(result.Value.Truncated);
    }

    [Fact]
    public async Task FailureAndCancellationArePreserved()
    {
        _reader.SearchBoundedAsync(Arg.Any<DirectorySearchQuery>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<BoundedDirectorySearchResult>(new(ErrorCode.AccessDenied, "Denied")));
        Assert.Equal(ErrorCode.AccessDenied, (await Create().ReadIdentityAsync(Query(ObjectId), CancellationToken.None)).Error!.Code);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().ReadIdentityAsync(Query(ObjectId), cancellation.Token));
    }
}
