using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.ActiveDirectory;
using Wec.Modules.ActiveDirectory.Application;
using Wec.Modules.ActiveDirectory.Domain;

namespace Wec.Modules.ActiveDirectory.Tests.Application;

public sealed class DirectoryOverviewServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 2, 20, 0, 0, TimeSpan.Zero);
    private const string DomainName = "corp.example.com";
    private const string NamingContext = "DC=corp,DC=example,DC=com";

    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();
    private readonly IDirectoryReader _directoryReader = Substitute.For<IDirectoryReader>();

    private DirectoryOverviewService CreateService()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new DirectoryOverviewService(
            _wmiQueryService,
            _directoryReader,
            clock,
            Options.Create(new ActiveDirectoryOptions()),
            NullLogger<DirectoryOverviewService>.Instance);
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

    [Fact]
    public async Task WorkgroupMachine_ReturnsNotJoinedWithoutTouchingTheDirectory()
    {
        SetUpComputerSystem(partOfDomain: false);

        Result<AdOverviewResult> result = await CreateService().GetOverviewAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.DomainJoined);
        Assert.Empty(result.Value.DomainControllers);
        await _directoryReader.DidNotReceiveWithAnyArgs().SearchAsync(default!, default);
    }

    [Fact]
    public async Task WmiFailure_PropagatesAsFailure()
    {
        _wmiQueryService.QueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<WmiInstance>>(Error.WmiUnavailable("WMI down")));

        Result<AdOverviewResult> result = await CreateService().GetOverviewAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.WmiUnavailable, result.Error!.Code);
    }

    [Fact]
    public async Task DomainJoined_BuildsOverviewFromDirectorySearches()
    {
        SetUpComputerSystem(partOfDomain: true);
        _directoryReader.SearchAsync(
                Arg.Is<DirectorySearchQuery>(query => query.Scope == DirectorySearchScope.Base),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DirectoryEntryData>>([
                Entry(string.Empty, ("defaultNamingContext", NamingContext)),
            ]));
        _directoryReader.SearchAsync(
                Arg.Is<DirectorySearchQuery>(query => query.LdapFilter.Contains(":=8192")),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DirectoryEntryData>>([
                Entry($"CN=DC01,OU=Domain Controllers,{NamingContext}", ("dNSHostName", "dc01.corp.example.com")),
            ]));
        _directoryReader.SearchAsync(
                Arg.Is<DirectorySearchQuery>(query =>
                    query.Attributes.Count == 0 && query.LdapFilter.Contains("objectClass=user")),
                Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<DirectoryEntryData>>([Entry("CN=a"), Entry("CN=b"), Entry("CN=c")]),
                Result.Success<IReadOnlyList<DirectoryEntryData>>([Entry("CN=disabled")]));
        _directoryReader.SearchAsync(
                Arg.Is<DirectorySearchQuery>(query => query.LdapFilter == "(objectCategory=group)"),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DirectoryEntryData>>([Entry("CN=g1"), Entry("CN=g2")]));
        _directoryReader.SearchAsync(
                Arg.Is<DirectorySearchQuery>(query => query.LdapFilter == "(objectCategory=computer)"),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DirectoryEntryData>>([Entry("CN=pc1")]));

        Result<AdOverviewResult> result = await CreateService().GetOverviewAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        AdOverviewResult overview = result.Value;
        Assert.True(overview.DomainJoined);
        Assert.Equal(DomainName, overview.DomainName);
        Assert.Equal(NamingContext, overview.DefaultNamingContext);
        Assert.Equal("dc01.corp.example.com", Assert.Single(overview.DomainControllers).HostName);
        Assert.Equal(3, overview.UserCount);
        Assert.Equal(1, overview.DisabledUserCount);
        Assert.Equal(2, overview.GroupCount);
        Assert.Equal(1, overview.ComputerCount);
        Assert.Equal(Now, overview.CapturedAtUtc);
    }

    [Fact]
    public async Task RootDseFailure_PropagatesDirectoryUnavailable()
    {
        SetUpComputerSystem(partOfDomain: true);
        _directoryReader.SearchAsync(Arg.Any<DirectorySearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<DirectoryEntryData>>(new Error(
                ErrorCode.DirectoryUnavailable, "LDAP server unreachable")));

        Result<AdOverviewResult> result = await CreateService().GetOverviewAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.DirectoryUnavailable, result.Error!.Code);
    }

    [Fact]
    public async Task MissingNamingContext_FailsVisiblyInsteadOfGuessing()
    {
        SetUpComputerSystem(partOfDomain: true);
        _directoryReader.SearchAsync(Arg.Any<DirectorySearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DirectoryEntryData>>([Entry(string.Empty)]));

        Result<AdOverviewResult> result = await CreateService().GetOverviewAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.DirectoryUnavailable, result.Error!.Code);
    }

    [Fact]
    public async Task AccessDeniedOnCount_PropagatesAccessDenied()
    {
        SetUpComputerSystem(partOfDomain: true);
        _directoryReader.SearchAsync(
                Arg.Is<DirectorySearchQuery>(query => query.Scope == DirectorySearchScope.Base),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DirectoryEntryData>>([
                Entry(string.Empty, ("defaultNamingContext", NamingContext)),
            ]));
        _directoryReader.SearchAsync(
                Arg.Is<DirectorySearchQuery>(query => query.Scope == DirectorySearchScope.Subtree),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<DirectoryEntryData>>(new Error(
                ErrorCode.AccessDenied, "Read refused")));

        Result<AdOverviewResult> result = await CreateService().GetOverviewAsync(CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.AccessDenied, result.Error!.Code);
    }
}
