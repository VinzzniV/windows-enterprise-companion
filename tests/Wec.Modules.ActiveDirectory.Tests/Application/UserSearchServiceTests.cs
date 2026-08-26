using System.Globalization;
using System.Security.Principal;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Core.Targets;
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

    private DirectoryUserReadService CreateDirectoryUserReadService()
    {
        IOptions<ActiveDirectoryOptions> options = Options.Create(new ActiveDirectoryOptions());
        return new DirectoryUserReadService(
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
        _directoryReader.SearchBoundedAsync(
                Arg.Is<DirectorySearchQuery>(query =>
                    query.Scope == DirectorySearchScope.Subtree && query.BaseDistinguishedName == ItOu),
                500,
                Arg.Any<CancellationToken>())
            .Returns(Result.Success(new BoundedDirectorySearchResult(2,
            [
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
            ])));

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
        _directoryReader.SearchBoundedAsync(
                Arg.Is<DirectorySearchQuery>(query => query.Scope == DirectorySearchScope.Subtree),
                500,
                Arg.Any<CancellationToken>())
            .Returns(Result.Success(new BoundedDirectorySearchResult(0, [])));

        Result<AdUserSearchResult> result = await CreateService()
            .SearchAsync(DirectoryConnection.Default, null, includeDisabled: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(NamingContext, result.Value.BaseDistinguishedName);
        await _directoryReader.Received(1).SearchBoundedAsync(
            Arg.Is<DirectorySearchQuery>(query =>
                query.BaseDistinguishedName == NamingContext && query.LdapFilter == AdFilters.EnabledUsers),
            500,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Search_TruncatesToTheConfiguredLimit()
    {
        SetUpDomainJoined();
        _directoryReader.SearchBoundedAsync(
                Arg.Is<DirectorySearchQuery>(query => query.Scope == DirectorySearchScope.Subtree),
                2,
                Arg.Any<CancellationToken>())
            .Returns(Result.Success(new BoundedDirectorySearchResult(3,
            [
                Entry("CN=A", new Dictionary<string, IReadOnlyList<string>> { ["sAMAccountName"] = ["a"] }),
                Entry("CN=B", new Dictionary<string, IReadOnlyList<string>> { ["sAMAccountName"] = ["b"] }),
            ])));

        Result<AdUserSearchResult> result = await CreateService(userSearchLimit: 2)
            .SearchAsync(DirectoryConnection.Default, ItOu, includeDisabled: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Users.Count);
        Assert.True(result.Value.Truncated);
    }

    [Fact]
    public async Task GetPage_UsesStableServerPagingAndMapsTheAllowlistedLifecycleProjection()
    {
        SetUpDomainJoined();
        var objectId = new Guid("00112233-4455-6677-8899-aabbccddeeff");
        var sid = new SecurityIdentifier("S-1-5-21-100-200-300-1104");
        byte[] sidBytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(sidBytes, 0);
        DateTimeOffset lastLogon = new(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset passwordSet = new(2026, 7, 1, 8, 30, 0, TimeSpan.Zero);
        DateTimeOffset passwordExpires = new(2026, 9, 1, 8, 30, 0, TimeSpan.Zero);
        _directoryReader.SearchPageAsync(
                Arg.Is<DirectorySearchQuery>(query =>
                    query.BaseDistinguishedName == ItOu
                    && query.LdapFilter.Contains("(department=IT)", StringComparison.Ordinal)
                    && query.LdapFilter.Contains("(displayName=*Alex\\2a*)", StringComparison.Ordinal)
                    && query.SortAttribute == "displayName"
                    && query.SortDescending
                    && query.SortTieBreakerAttribute == "sAMAccountName"
                    && query.Attributes.Contains("objectGUID", StringComparer.Ordinal)
                    && query.Attributes.Contains("msDS-UserPasswordExpiryTimeComputed", StringComparer.Ordinal)),
                25,
                25,
                Arg.Any<CancellationToken>())
            .Returns(Result.Success(new BoundedDirectorySearchResult(61,
            [
                Entry($"CN=Alex Example,{ItOu}", new Dictionary<string, IReadOnlyList<string>>
                {
                    ["objectGUID"] = [Convert.ToBase64String(objectId.ToByteArray())],
                    ["objectSid"] = [Convert.ToBase64String(sidBytes)],
                    ["displayName"] = ["Alex Example"],
                    ["sAMAccountName"] = ["a.example"],
                    ["userPrincipalName"] = ["a.example@kauth.local"],
                    ["mail"] = ["alex@example.test"],
                    ["employeeID"] = ["E-1042"],
                    ["department"] = ["IT"],
                    ["title"] = ["Administrator"],
                    ["manager"] = [$"CN=Manager,{ItOu}"],
                    ["userAccountControl"] = [(512 + AdFilters.UacPasswordNeverExpires).ToString(CultureInfo.InvariantCulture)],
                    ["whenCreated"] = ["20260102130405.0Z"],
                    ["accountExpires"] = [long.MaxValue.ToString(CultureInfo.InvariantCulture)],
                    ["lastLogonTimestamp"] = [lastLogon.ToFileTime().ToString(CultureInfo.InvariantCulture)],
                    ["pwdLastSet"] = [passwordSet.ToFileTime().ToString(CultureInfo.InvariantCulture)],
                    ["msDS-UserPasswordExpiryTimeComputed"] = [passwordExpires.ToFileTime().ToString(CultureInfo.InvariantCulture)],
                    ["memberOf"] = ["CN=GG-App,OU=Groups,DC=kauth,DC=local"],
                }),
            ])));
        var query = new DirectoryUserPageQuery(
            new DirectoryUserReadConnection(null, null, ScanCredentials.CurrentUser),
            "Alex*",
            ItOu,
            "IT",
            DirectoryUserAccountStateFilter.Enabled,
            Page: 2,
            PageSize: 25,
            DirectoryUserSortField.DisplayName,
            DirectoryUserSortDirection.Descending);

        Result<DirectoryUserPage> result = await CreateDirectoryUserReadService()
            .GetPageAsync(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(61, result.Value.TotalCount);
        Assert.Equal(2, result.Value.Page);
        DirectoryUserRecord user = Assert.Single(result.Value.Users);
        Assert.Equal(objectId, user.ObjectId);
        Assert.Equal(sid.Value, user.Sid);
        Assert.Equal("Alex Example", user.DisplayName);
        Assert.Equal("E-1042", user.EmployeeId);
        Assert.True(user.Enabled);
        Assert.True(user.PasswordNeverExpires);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 13, 4, 5, TimeSpan.Zero), user.CreatedAtUtc);
        Assert.Null(user.AccountExpiresAtUtc);
        Assert.Equal(lastLogon, user.ReplicatedLastLogonAtUtc);
        Assert.Equal(passwordSet, user.PasswordLastSetAtUtc);
        Assert.Equal(passwordExpires, user.PasswordExpiresAtUtc);
        Assert.Equal(ItOu, user.OrganizationalUnitPath);
        Assert.Equal("GG-App", Assert.Single(user.DirectGroups).Name);
    }

    [Fact]
    public async Task GetPage_RejectsAnUnboundedPageBeforeReadingTheDirectory()
    {
        var query = new DirectoryUserPageQuery(
            new DirectoryUserReadConnection(null, null, ScanCredentials.CurrentUser),
            null,
            null,
            null,
            DirectoryUserAccountStateFilter.All,
            Page: 1,
            PageSize: 101,
            DirectoryUserSortField.SamAccountName,
            DirectoryUserSortDirection.Ascending);

        Result<DirectoryUserPage> result = await CreateDirectoryUserReadService()
            .GetPageAsync(query, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, result.Error!.Code);
        await _directoryReader.DidNotReceiveWithAnyArgs().SearchPageAsync(default!, 0, 0, default);
    }

    [Fact]
    public async Task GetById_QueriesTheStableObjectGuidAndReturnsNullWhenItIsAbsent()
    {
        SetUpDomainJoined();
        var objectId = new Guid("00112233-4455-6677-8899-aabbccddeeff");
        _directoryReader.SearchBoundedAsync(
                Arg.Is<DirectorySearchQuery>(query =>
                    query.LdapFilter == AdFilters.UserByObjectGuid(objectId)
                    && query.Attributes.Contains("objectGUID", StringComparer.Ordinal)),
                1,
                Arg.Any<CancellationToken>())
            .Returns(Result.Success(new BoundedDirectorySearchResult(0, [])));

        Result<DirectoryUserRecord?> result = await CreateDirectoryUserReadService().GetByIdAsync(
            new DirectoryUserIdentityQuery(
                new DirectoryUserReadConnection(null, null, ScanCredentials.CurrentUser),
                objectId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }
}
