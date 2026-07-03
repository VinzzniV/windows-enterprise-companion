using System.Security.Principal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.ActiveDirectory.Application;
using Wec.Modules.ActiveDirectory.Domain;

namespace Wec.Modules.ActiveDirectory.Tests.Application;

public sealed class DirectoryHygieneServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 2, 20, 0, 0, TimeSpan.Zero);
    private const string DomainName = "corp.example.com";
    private const string NamingContext = "DC=corp,DC=example,DC=com";
    private const string DomainSid = "S-1-5-21-1111111111-2222222222-3333333333";

    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();
    private readonly IDirectoryReader _directoryReader = Substitute.For<IDirectoryReader>();
    private readonly ActiveDirectoryOptions _options = new() { ExampleLimit = 2 };

    private DirectoryHygieneService CreateService()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        IOptions<ActiveDirectoryOptions> options = Options.Create(_options);
        return new DirectoryHygieneService(
            new DomainContextService(_wmiQueryService, _directoryReader, options),
            _directoryReader,
            clock,
            options,
            NullLogger<DirectoryHygieneService>.Instance);
    }

    private void SetUpComputerSystem(bool partOfDomain) =>
        _wmiQueryService.QueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<WmiInstance>>([
                new WmiInstance(new Dictionary<string, object?>
                {
                    ["PartOfDomain"] = partOfDomain,
                    ["Domain"] = partOfDomain ? DomainName : "WORKGROUP",
                }),
            ]));

    private static DirectoryEntryData Entry(string dn, params (string Name, string Value)[] attributes) =>
        new(dn, attributes.ToDictionary(
            attribute => attribute.Name,
            attribute => (IReadOnlyList<string>)[attribute.Value],
            StringComparer.OrdinalIgnoreCase));

    private static string DomainSidAsBase64()
    {
        var sid = new SecurityIdentifier(DomainSid);
        byte[] binary = new byte[sid.BinaryLength];
        sid.GetBinaryForm(binary, 0);
        return Convert.ToBase64String(binary);
    }

    private void SetUpDomainScaffolding()
    {
        SetUpComputerSystem(partOfDomain: true);
        _directoryReader.SearchAsync(
                Arg.Is<DirectorySearchQuery>(query =>
                    query.Scope == DirectorySearchScope.Base && query.BaseDistinguishedName.Length == 0),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DirectoryEntryData>>([
                Entry(string.Empty, ("defaultNamingContext", NamingContext)),
            ]));
        _directoryReader.SearchAsync(
                Arg.Is<DirectorySearchQuery>(query =>
                    query.Scope == DirectorySearchScope.Base && query.BaseDistinguishedName == NamingContext),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DirectoryEntryData>>([
                Entry(NamingContext, ("objectSid", DomainSidAsBase64())),
            ]));
        // Default for every subtree search: empty result; tests override per filter
        _directoryReader.SearchAsync(
                Arg.Is<DirectorySearchQuery>(query => query.Scope == DirectorySearchScope.Subtree),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DirectoryEntryData>>([]));
    }

    [Fact]
    public async Task WorkgroupMachine_ReturnsNotJoinedWithoutTouchingTheDirectory()
    {
        SetUpComputerSystem(partOfDomain: false);

        Result<AdHygieneResult> result = await CreateService().GetHygieneAsync(DirectoryConnection.Default, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.DomainJoined);
        await _directoryReader.DidNotReceiveWithAnyArgs().SearchAsync(default!, default);
    }

    [Fact]
    public async Task InactiveUsersRule_UsesFileTimeCutoffFromClockAndThreshold()
    {
        SetUpDomainScaffolding();
        long expectedCutoff = Now.Subtract(TimeSpan.FromDays(90)).UtcDateTime.ToFileTimeUtc();

        Result<AdHygieneResult> result = await CreateService().GetHygieneAsync(DirectoryConnection.Default, CancellationToken.None);

        Assert.True(result.IsSuccess);
        await _directoryReader.Received(1).SearchAsync(
            Arg.Is<DirectorySearchQuery>(query =>
                query.LdapFilter.Contains($"lastLogonTimestamp<={expectedCutoff}") &&
                query.LdapFilter.Contains("objectClass=user")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AccountRule_KeepsExactCountButBoundsExamples()
    {
        SetUpDomainScaffolding();
        long staleFileTime = Now.Subtract(TimeSpan.FromDays(200)).UtcDateTime.ToFileTimeUtc();
        _directoryReader.SearchAsync(
                Arg.Is<DirectorySearchQuery>(query =>
                    query.LdapFilter.Contains("lastLogonTimestamp") &&
                    query.LdapFilter.Contains("objectClass=user")),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DirectoryEntryData>>([
                Entry("CN=a", ("sAMAccountName", "a"),
                    ("lastLogonTimestamp", staleFileTime.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                Entry("CN=b", ("sAMAccountName", "b")),
                Entry("CN=c", ("sAMAccountName", "c")),
            ]));

        Result<AdHygieneResult> result = await CreateService().GetHygieneAsync(DirectoryConnection.Default, CancellationToken.None);

        AdHygieneRule rule = Assert.Single(result.Value.Rules, r => r.RuleId == "WEC-AD-INACTIVE-USERS");
        Assert.Equal(3, rule.MatchCount);
        Assert.Equal(2, rule.Examples.Count);
        Assert.Equal(
            new DateTimeOffset(DateTime.FromFileTimeUtc(staleFileTime), TimeSpan.Zero),
            rule.Examples[0].LastLogonUtc);
        Assert.Null(rule.Examples[1].LastLogonUtc);
    }

    [Fact]
    public async Task PasswordNeverExpiresRule_MatchesOnlyEnabledAccounts()
    {
        SetUpDomainScaffolding();

        await CreateService().GetHygieneAsync(DirectoryConnection.Default, CancellationToken.None);

        await _directoryReader.Received(1).SearchAsync(
            Arg.Is<DirectorySearchQuery>(query =>
                query.LdapFilter.Contains(":=65536") && query.LdapFilter.Contains("(!(userAccountControl")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PrivilegedGroups_AreResolvedBySidAndAbsentGroupsAreSkipped()
    {
        SetUpDomainScaffolding();
        _directoryReader.SearchAsync(
                Arg.Is<DirectorySearchQuery>(query =>
                    query.LdapFilter == AdFilters.GroupBySid($"{DomainSid}-512")),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DirectoryEntryData>>([
                new DirectoryEntryData($"CN=Domänen-Admins,CN=Users,{NamingContext}",
                    new Dictionary<string, IReadOnlyList<string>>
                    {
                        ["sAMAccountName"] = ["Domänen-Admins"],
                        ["member"] = [$"CN=Admin,{NamingContext}", $"CN=Break Glass,{NamingContext}", $"CN=Svc,{NamingContext}"],
                    }),
            ]));

        Result<AdHygieneResult> result = await CreateService().GetHygieneAsync(DirectoryConnection.Default, CancellationToken.None);

        Assert.True(result.IsSuccess);
        PrivilegedGroupInfo group = Assert.Single(result.Value.PrivilegedGroups);
        Assert.Equal("Domänen-Admins", group.GroupName);
        Assert.Equal(3, group.DirectMemberCount);
        Assert.Equal(2, group.MemberDistinguishedNames.Count);
        await _directoryReader.Received(1).SearchAsync(
            Arg.Is<DirectorySearchQuery>(query =>
                query.LdapFilter == AdFilters.GroupBySid("S-1-5-32-544")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisabledPrivilegedRule_FiltersOnEscapedGroupDns()
    {
        SetUpDomainScaffolding();
        string trickyGroupDn = $"CN=Admins (Tier 0),CN=Users,{NamingContext}";
        _directoryReader.SearchAsync(
                Arg.Is<DirectorySearchQuery>(query =>
                    query.LdapFilter == AdFilters.GroupBySid($"{DomainSid}-512")),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DirectoryEntryData>>([
                Entry(trickyGroupDn, ("sAMAccountName", "Domain Admins")),
            ]));

        await CreateService().GetHygieneAsync(DirectoryConnection.Default, CancellationToken.None);

        await _directoryReader.Received(1).SearchAsync(
            Arg.Is<DirectorySearchQuery>(query =>
                query.LdapFilter.Contains(@"CN=Admins \28Tier 0\29") &&
                query.LdapFilter.Contains(":=2)")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DirectoryFailureDuringRules_PropagatesTypedError()
    {
        SetUpDomainScaffolding();
        _directoryReader.SearchAsync(
                Arg.Is<DirectorySearchQuery>(query => query.LdapFilter.Contains(":=65536")),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<DirectoryEntryData>>(new Error(
                ErrorCode.AccessDenied, "Read refused")));

        Result<AdHygieneResult> result = await CreateService().GetHygieneAsync(DirectoryConnection.Default, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.AccessDenied, result.Error!.Code);
    }
}
