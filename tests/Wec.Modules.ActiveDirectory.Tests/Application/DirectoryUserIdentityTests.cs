using System.Security.Principal;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.ActiveDirectory.Application;

namespace Wec.Modules.ActiveDirectory.Tests.Application;

public sealed class DirectoryUserIdentityTests
{
    private const string Scope = "example.test";
    private const string Sid = "S-1-5-21-1-2-3-1001";
    private static readonly Guid ObjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DirectoryUserReadConnection Connection = new(Scope, null, ScanCredentials.CurrentUser);
    private readonly IDirectoryReader _reader = Substitute.For<IDirectoryReader>();

    public DirectoryUserIdentityTests()
    {
        _reader.SearchAsync(Arg.Any<DirectorySearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DirectoryEntryData>>([new("", new Dictionary<string, IReadOnlyList<string>>
            {
                ["defaultNamingContext"] = ["DC=example,DC=test"],
            })]));
        Entries(User(ObjectId));
    }

    private DirectoryUserReadService Create()
    {
        var options = Options.Create(new ActiveDirectoryOptions());
        return new(new DomainContextService(Substitute.For<IWmiQueryService>(), _reader, options), _reader,
            new PrivilegedGroupResolver(_reader, options), options);
    }

    private void Entries(params DirectoryEntryData[] entries) => _reader.SearchBoundedAsync(Arg.Any<DirectorySearchQuery>(),
        Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new BoundedDirectorySearchResult(entries.Length, entries)));

    private static DirectoryEntryData User(Guid id)
    {
        var sid = new SecurityIdentifier(Sid);
        var bytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(bytes, 0);
        return new("CN=Account,DC=example,DC=test", new Dictionary<string, IReadOnlyList<string>>
        {
            ["objectGUID"] = [Convert.ToBase64String(id.ToByteArray())], ["objectSid"] = [Convert.ToBase64String(bytes)],
            ["sAMAccountName"] = ["account"], ["displayName"] = ["Display name"],
        });
    }

    [Fact]
    public async Task ExactSidResolvesTheDirectoryGuidWithoutAnAccountNameFallback()
    {
        var result = await Create().GetBySidAsync(new(Connection, Sid, Scope), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(ObjectId, result.Value!.ObjectId);
        Assert.Equal(Scope, result.Value.DirectoryScope);
        Assert.Null(result.Value.Enabled);
        await _reader.Received(1).SearchBoundedAsync(Arg.Is<DirectorySearchQuery>(query =>
            query.LdapFilter == $"(&(objectCategory=person)(objectClass=user)(objectSid={Sid}))"), 2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicateSidNeverSelectsTheFirstAccount()
    {
        Entries(User(ObjectId), User(Guid.NewGuid()));
        var result = await Create().GetBySidAsync(new(Connection, Sid, Scope), CancellationToken.None);
        Assert.Equal(ErrorCode.DirectoryUnavailable, result.Error!.Code);
    }

    [Fact]
    public async Task GuidResponseMustCarryTheRequestedGuid()
    {
        Entries(User(Guid.NewGuid()));
        Assert.True((await Create().GetByIdAsync(new(Connection, ObjectId, Scope), CancellationToken.None)).IsFailure);
    }

    [Fact]
    public async Task WrongDirectoryScopePreventsIdentitySearch()
    {
        var result = await Create().GetByIdAsync(new(Connection, ObjectId, "other.test"), CancellationToken.None);
        Assert.True(result.IsFailure);
        await _reader.DidNotReceiveWithAnyArgs().SearchBoundedAsync(default!, default, default);
    }

    [Theory]
    [InlineData("account@example.test")]
    [InlineData("S-1-5-21-1-2-3-1001)(objectClass=*)")]
    public async Task InvalidSidNeverReachesLdap(string sid)
    {
        Assert.True((await Create().GetBySidAsync(new(Connection, sid), CancellationToken.None)).IsFailure);
        await _reader.DidNotReceiveWithAnyArgs().SearchAsync(default!, default);
    }

    [Fact]
    public async Task CancelledLookupDoesNotSearchTheDirectory()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().GetBySidAsync(new(Connection, Sid), cancellation.Token));
        await _reader.DidNotReceiveWithAnyArgs().SearchAsync(default!, default);
    }
}
