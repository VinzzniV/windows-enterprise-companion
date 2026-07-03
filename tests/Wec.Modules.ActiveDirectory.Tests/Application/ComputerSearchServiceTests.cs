using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.ActiveDirectory;
using Wec.Modules.ActiveDirectory.Application;

namespace Wec.Modules.ActiveDirectory.Tests.Application;

public sealed class ComputerSearchServiceTests
{
    private const string DomainName = "kauth.local";
    private const string NamingContext = "DC=kauth,DC=local";

    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();
    private readonly IDirectoryReader _directoryReader = Substitute.For<IDirectoryReader>();

    private ComputerSearchService CreateService(int computerSearchLimit = 500)
    {
        IOptions<ActiveDirectoryOptions> options = Options.Create(new ActiveDirectoryOptions
        {
            ComputerSearchLimit = computerSearchLimit,
        });
        return new ComputerSearchService(
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
                Entry(string.Empty, ("defaultNamingContext", NamingContext)),
            ]));
    }

    private void SetUpComputerEntries(params DirectoryEntryData[] entries) =>
        _directoryReader.SearchAsync(
                Arg.Is<DirectorySearchQuery>(query => query.Scope == DirectorySearchScope.Subtree),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DirectoryEntryData>>(entries));

    private static DirectoryEntryData Entry(string dn, params (string Name, string Value)[] attributes) =>
        new(dn, attributes.ToDictionary(
            attribute => attribute.Name,
            attribute => (IReadOnlyList<string>)[attribute.Value],
            StringComparer.OrdinalIgnoreCase));

    [Fact]
    public async Task WorkgroupMachine_ReturnsNotJoinedWithoutSearching()
    {
        _wmiQueryService.QueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<WmiInstance>>([
                new WmiInstance(new Dictionary<string, object?>
                {
                    ["PartOfDomain"] = false,
                    ["Domain"] = "WORKGROUP",
                }),
            ]));

        Result<AdComputerSearchResult> result = await CreateService()
            .SearchAsync(DirectoryConnection.Default, null, false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.DomainJoined);
        Assert.Empty(result.Value.Computers);
        await _directoryReader.DidNotReceiveWithAnyArgs().SearchAsync(default!, default);
    }

    [Fact]
    public async Task Search_MapsComputersSortedByNameWithEnabledFlag()
    {
        SetUpDomainJoined();
        SetUpComputerEntries(
            Entry("CN=PC2", ("name", "PC2"), ("dNSHostName", "pc2.kauth.local"),
                ("operatingSystem", "Windows 11 Pro"), ("userAccountControl", "4096")),
            Entry("CN=PC1", ("name", "PC1"), ("dNSHostName", "pc1.kauth.local"),
                ("operatingSystem", "Windows 10 Pro"), ("userAccountControl", "4098")));

        Result<AdComputerSearchResult> result = await CreateService()
            .SearchAsync(DirectoryConnection.Default, "pc", true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DomainName, result.Value.DomainName);
        Assert.Equal(["PC1", "PC2"], result.Value.Computers.Select(computer => computer.Name));
        Assert.False(result.Value.Computers[0].Enabled); // 4098 carries the disabled bit
        Assert.True(result.Value.Computers[1].Enabled);
        Assert.Equal("pc1.kauth.local", result.Value.Computers[0].DnsHostName);
        Assert.False(result.Value.Truncated);
    }

    [Fact]
    public async Task Search_TruncatesToTheConfiguredLimit()
    {
        SetUpDomainJoined();
        SetUpComputerEntries(
            Entry("CN=A", ("name", "A"), ("userAccountControl", "4096")),
            Entry("CN=B", ("name", "B"), ("userAccountControl", "4096")),
            Entry("CN=C", ("name", "C"), ("userAccountControl", "4096")));

        Result<AdComputerSearchResult> result = await CreateService(computerSearchLimit: 2)
            .SearchAsync(DirectoryConnection.Default, null, false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Computers.Count);
        Assert.True(result.Value.Truncated);
    }

    [Fact]
    public async Task SearchFailure_Propagates()
    {
        SetUpDomainJoined();
        _directoryReader.SearchAsync(
                Arg.Is<DirectorySearchQuery>(query => query.Scope == DirectorySearchScope.Subtree),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<DirectoryEntryData>>(
                new Error(ErrorCode.AccessDenied, "read refused")));

        Result<AdComputerSearchResult> result = await CreateService()
            .SearchAsync(DirectoryConnection.Default, null, false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.AccessDenied, result.Error!.Code);
    }

    [Theory]
    [InlineData(null, false,
        "(&(objectCategory=computer)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))")]
    [InlineData(null, true, "(objectCategory=computer)")]
    [InlineData("srv", false,
        "(&(objectCategory=computer)(|(name=*srv*)(dNSHostName=*srv*))(!(userAccountControl:1.2.840.113556.1.4.803:=2)))")]
    [InlineData("PK-*", true,
        "(&(objectCategory=computer)(|(name=PK-*)(dNSHostName=PK-*)))")]
    [InlineData("a(b)", true,
        @"(&(objectCategory=computer)(|(name=*a\28b\29*)(dNSHostName=*a\28b\29*)))")]
    public void ComputersByName_BuildsTheExpectedFilter(
        string? pattern, bool includeDisabled, string expected)
    {
        Assert.Equal(expected, AdFilters.ComputersByName(pattern, includeDisabled));
    }
}
