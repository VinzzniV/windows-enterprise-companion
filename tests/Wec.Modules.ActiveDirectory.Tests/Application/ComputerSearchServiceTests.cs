using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Core.Targets;
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
        _directoryReader.SearchBoundedAsync(
                Arg.Is<DirectorySearchQuery>(query => query.Scope == DirectorySearchScope.Subtree),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Result.Success(new BoundedDirectorySearchResult(
                entries.Length,
                entries.Take(call.ArgAt<int>(1)).ToList())));

    private static DirectoryEntryData Entry(string dn, params (string Name, string Value)[] attributes) =>
        new(dn, attributes.ToDictionary(
            attribute => attribute.Name,
            attribute => (IReadOnlyList<string>)[attribute.Value],
            StringComparer.OrdinalIgnoreCase));

    [Fact]
    public async Task ComputerInventoryScopeUsesActualNamingContextInsteadOfConfiguredAlias()
    {
        SetUpDomainJoined();
        SetUpComputerEntries(Entry("CN=PC,DC=kauth,DC=local", ("name", "PC")));
        var result = await CreateService().SearchAsync(new DirectoryConnection("KAUTH", null, ScanCredentials.CurrentUser), null, true, CancellationToken.None);
        Assert.Equal("kauth.local", result.Value.DomainName);
        Assert.Equal("kauth.local", Assert.Single(result.Value.Computers).DirectoryScope);
    }

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
                ("operatingSystem", "Windows 11 Pro"), ("userAccountControl", "4096"),
                ("lastLogonTimestamp", new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero)
                    .ToFileTime().ToString(System.Globalization.CultureInfo.InvariantCulture))),
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
        Assert.Equal("CN=PC1", result.Value.Computers[0].DistinguishedName);
        Assert.Equal(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero), result.Value.Computers[1].LastLogonDate);
        Assert.False(result.Value.Truncated);
    }

    [Fact]
    public async Task Inventory_LoadsDisabledComputersAndMapsRequiredFields()
    {
        SetUpDomainJoined();
        var lastLogon = new DateTimeOffset(2026, 8, 1, 8, 30, 0, TimeSpan.Zero);
        SetUpComputerEntries(
            Entry("CN=PC1,OU=Clients,DC=kauth,DC=local", ("name", "PC1"),
                ("userAccountControl", "4098"),
                ("lastLogonTimestamp", lastLogon.ToFileTime().ToString(
                    System.Globalization.CultureInfo.InvariantCulture))));

        Result<AdComputerInventory> result = await CreateService().LoadAsync(
            new AdComputerInventoryQuery(null, null, ScanCredentials.CurrentUser, 10),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        AdComputerInventoryItem computer = Assert.Single(result.Value.Computers);
        Assert.Equal("PC1", computer.ComputerName);
        Assert.False(computer.Enabled);
        Assert.Equal("CN=PC1,OU=Clients,DC=kauth,DC=local", computer.DistinguishedName);
        Assert.Equal(lastLogon, computer.LastLogonDate);
    }

    [Fact]
    public async Task Inventory_PreservesScopedIdentityAndUnknownAccountState()
    {
        SetUpDomainJoined();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        SetUpComputerEntries(
            Entry("CN=PC1,OU=Old,DC=kauth,DC=local", ("name", "PC1"),
                ("objectGUID", Convert.ToBase64String(first.ToByteArray()))),
            Entry("CN=PC1,OU=New,DC=kauth,DC=local", ("name", "PC1"),
                ("objectGUID", Convert.ToBase64String(second.ToByteArray()))));

        Result<AdComputerInventory> result = await CreateService().LoadAsync(
            new AdComputerInventoryQuery(null, null, ScanCredentials.CurrentUser, 10), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Computers.Count);
        Assert.Equal(new Guid?[] { first, second }, result.Value.Computers.Select(computer => computer.ObjectId));
        Assert.All(result.Value.Computers, computer =>
        {
            Assert.Null(computer.Enabled);
            Assert.Null(computer.SecurityIdentifier);
            Assert.Equal(DomainName, computer.DirectoryScope);
        });
        await _directoryReader.Received(1).SearchBoundedAsync(
            Arg.Is<DirectorySearchQuery>(query => query.Attributes.Contains("objectGUID")
                && query.Attributes.Contains("objectSid") && !query.Attributes.Contains("*")),
            10,
            Arg.Any<CancellationToken>());
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
    public async Task Search_UsesTheExplicitBoundForATypeaheadLookup()
    {
        SetUpDomainJoined();
        SetUpComputerEntries(
            Entry("CN=A", ("name", "A"), ("userAccountControl", "4096")),
            Entry("CN=B", ("name", "B"), ("userAccountControl", "4096")));

        Result<AdComputerSearchResult> result = await CreateService(computerSearchLimit: 500)
            .SearchAsync(
                DirectoryConnection.Default,
                "pc",
                includeDisabled: false,
                CancellationToken.None,
                resultLimit: 1);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Computers);
        Assert.True(result.Value.Truncated);
        await _directoryReader.Received(1).SearchBoundedAsync(
            Arg.Any<DirectorySearchQuery>(),
            1,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchFailure_Propagates()
    {
        SetUpDomainJoined();
        _directoryReader.SearchBoundedAsync(
                Arg.Is<DirectorySearchQuery>(query => query.Scope == DirectorySearchScope.Subtree),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<BoundedDirectorySearchResult>(
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
