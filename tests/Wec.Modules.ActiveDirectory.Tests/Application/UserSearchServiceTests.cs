using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.ActiveDirectory;
using Wec.Modules.ActiveDirectory.Application;

namespace Wec.Modules.ActiveDirectory.Tests.Application;

public sealed class UserSearchServiceTests
{
    private const string DomainName = "kauth.local";
    private const string NamingContext = "DC=kauth,DC=local";
    private const string ItOu = "OU=IT,DC=kauth,DC=local";

    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();
    private readonly IDirectoryReader _directoryReader = Substitute.For<IDirectoryReader>();

    private UserSearchService CreateService(int userSearchLimit = 500)
    {
        IOptions<ActiveDirectoryOptions> options = Options.Create(new ActiveDirectoryOptions
        {
            UserSearchLimit = userSearchLimit,
        });
        return new UserSearchService(
            new DomainContextService(_wmiQueryService, _directoryReader, options),
            _directoryReader,
            options);
    }

    private void SetUpDomainJoined()
    {
        _wmiQueryService.QueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<WmiInstance>>([
                new WmiInstance(new Dictionary<string, object?>
                {
                    ["PartOfDomain"] = true,
                    ["Domain"] = DomainName,
                }),
            ]));
        _directoryReader.SearchAsync(
                Arg.Is<DirectorySearchQuery>(query => query.Scope == DirectorySearchScope.Base),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DirectoryEntryData>>([
                Entry(string.Empty, new Dictionary<string, IReadOnlyList<string>>
                {
                    ["defaultNamingContext"] = [NamingContext],
                }),
            ]));
    }

    private static DirectoryEntryData Entry(string dn, Dictionary<string, IReadOnlyList<string>> attributes) =>
        new(dn, attributes);

    [Fact]
    public async Task Search_UsesTheOuAsSearchBaseAndMapsUsersWithGroups()
    {
        SetUpDomainJoined();
        _directoryReader.SearchAsync(
                Arg.Is<DirectorySearchQuery>(query =>
                    query.Scope == DirectorySearchScope.Subtree && query.BaseDistinguishedName == ItOu),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DirectoryEntryData>>([
                Entry($"CN=Max Muster,{ItOu}", new Dictionary<string, IReadOnlyList<string>>
                {
                    ["displayName"] = ["Max Muster"],
                    ["sAMAccountName"] = ["m.muster"],
                    ["userPrincipalName"] = ["m.muster@kauth.local"],
                    ["userAccountControl"] = ["512"],
                    ["memberOf"] = ["CN=GG-App-Habel,OU=Groups,DC=kauth,DC=local", "CN=GG-Drive-IT,OU=Groups,DC=kauth,DC=local"],
                }),
                Entry($"CN=Alt Account,{ItOu}", new Dictionary<string, IReadOnlyList<string>>
                {
                    ["sAMAccountName"] = ["a.account"],
                    ["userAccountControl"] = ["514"],
                }),
            ]));

        Result<AdUserSearchResult> result = await CreateService()
            .SearchAsync(DirectoryConnection.Default, ItOu, includeDisabled: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ItOu, result.Value.BaseDistinguishedName);
        Assert.Equal(2, result.Value.Users.Count);

        AdUser disabled = result.Value.Users[0]; // "a.account" sorts before "Max Muster"
        Assert.Equal("a.account", disabled.Name);
        Assert.False(disabled.Enabled);

        AdUser enabled = result.Value.Users[1];
        Assert.Equal("Max Muster", enabled.Name);
        Assert.True(enabled.Enabled);
        Assert.Equal("m.muster@kauth.local", enabled.UserPrincipalName);
        Assert.Equal(["GG-App-Habel", "GG-Drive-IT"], enabled.Groups);
    }

    [Fact]
    public async Task Search_WithoutBaseDn_FallsBackToTheNamingContext()
    {
        SetUpDomainJoined();
        _directoryReader.SearchAsync(
                Arg.Is<DirectorySearchQuery>(query => query.Scope == DirectorySearchScope.Subtree),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DirectoryEntryData>>([]));

        Result<AdUserSearchResult> result = await CreateService()
            .SearchAsync(DirectoryConnection.Default, null, includeDisabled: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(NamingContext, result.Value.BaseDistinguishedName);
        await _directoryReader.Received(1).SearchAsync(
            Arg.Is<DirectorySearchQuery>(query =>
                query.BaseDistinguishedName == NamingContext && query.LdapFilter == AdFilters.EnabledUsers),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Search_TruncatesToTheConfiguredLimit()
    {
        SetUpDomainJoined();
        _directoryReader.SearchAsync(
                Arg.Is<DirectorySearchQuery>(query => query.Scope == DirectorySearchScope.Subtree),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DirectoryEntryData>>([
                Entry("CN=A", new Dictionary<string, IReadOnlyList<string>> { ["sAMAccountName"] = ["a"] }),
                Entry("CN=B", new Dictionary<string, IReadOnlyList<string>> { ["sAMAccountName"] = ["b"] }),
                Entry("CN=C", new Dictionary<string, IReadOnlyList<string>> { ["sAMAccountName"] = ["c"] }),
            ]));

        Result<AdUserSearchResult> result = await CreateService(userSearchLimit: 2)
            .SearchAsync(DirectoryConnection.Default, ItOu, includeDisabled: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Users.Count);
        Assert.True(result.Value.Truncated);
    }
}
