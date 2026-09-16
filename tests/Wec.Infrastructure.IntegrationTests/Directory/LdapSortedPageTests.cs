using System.DirectoryServices.Protocols;
using Microsoft.Extensions.Logging.Abstractions;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Infrastructure.Directory;

namespace Wec.Infrastructure.IntegrationTests.Directory;

public sealed class LdapSortedPageTests
{
    private static DirectorySearchQuery Query => new("example.test", "DC=example,DC=test", "(objectClass=*)",
        ["name", "sAMAccountName"], DirectorySearchScope.Subtree, 500, TimeSpan.FromSeconds(30),
        SortAttribute: "name", SortTieBreakerAttribute: "sAMAccountName", MaximumSortedPageEntries: 100);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WireRequestUsesOnlyOneAdSupportedSortKey(bool descending)
    {
        SearchRequest request = LdapDirectoryReader.CreateSearchRequest(Query with { SortDescending = descending });
        SortRequestControl control = Assert.Single(request.Controls.OfType<SortRequestControl>());
        SortKey key = Assert.Single(control.SortKeys);
        Assert.Equal("name", key.AttributeName);
        Assert.Equal(descending, key.ReverseOrder);
        Assert.True(control.IsCritical);
        Assert.Equal(500, Assert.Single(request.Controls.OfType<PageResultRequestControl>()).PageSize);
        Assert.Equal(new[] { "name", "sAMAccountName" }, request.Attributes.Cast<string>());
        Assert.NotEmpty(control.GetValue());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OffsetPagesPreserveDuplicateAndMissingNamesRegardlessOfSourceOrder(bool descending)
    {
        DirectoryEntryData[] source = [Entry("c", "Same", "duplicate"), Entry("a", "Same", "duplicate"),
            Entry("b", "Same", null), Entry("d", null, null), Entry("e", "Alpha", "first")];
        DirectorySearchQuery query = Query with { SortDescending = descending };
        var whole = new BoundedDirectorySortAccumulator(query, 0, 5);
        foreach (var entry in source) { whole.Add(entry); }
        string[] expected = descending ? ["d", "b", "c", "a", "e"] : ["e", "a", "c", "b", "d"];
        Assert.Equal(expected.Select(Dn), whole.Build().Entries.Select(entry => entry.DistinguishedName));
        for (int offset = 0; offset < source.Length; offset++)
        {
            var page = new BoundedDirectorySortAccumulator(query, offset, 1);
            foreach (var entry in source.Reverse()) { page.Add(entry); }
            Assert.Equal(source.Length, page.Build().TotalCount);
            Assert.Equal(Dn(expected[offset]), Assert.Single(page.Build().Entries).DistinguishedName);
        }
    }

    [Fact]
    public void LargeFixtureRetainsOnlyBoundedPrefixAndCountsAllEntries()
    {
        var accumulator = new BoundedDirectorySortAccumulator(Query, 40, 10);
        for (int index = 24_999; index >= 0; index--)
        {
            accumulator.Add(Entry(index.ToString("D5", System.Globalization.CultureInfo.InvariantCulture), "Same", null));
            Assert.InRange(accumulator.RetainedCount, 0, 50);
        }
        var result = accumulator.Build();
        Assert.Equal(25_000, result.TotalCount);
        Assert.Equal(10, result.Entries.Count);
        Assert.Equal(Dn("00040"), result.Entries[0].DistinguishedName);
        Assert.Equal(Dn("00049"), result.Entries[^1].DistinguishedName);
    }

    [Fact]
    public void FileTimesSortNumericallyAndUnicodeNamesRemainDistinct()
    {
        var query = Query with { SortAttribute = "lastLogonTimestamp" };
        var accumulator = new BoundedDirectorySortAccumulator(query, 0, 3);
        foreach (string value in new[] { "100", "20", "3" })
        {
            accumulator.Add(new(Dn(value), new Dictionary<string, IReadOnlyList<string>> { ["lastLogonTimestamp"] = [value] }));
        }
        Assert.Equal(new[] { Dn("3"), Dn("20"), Dn("100") }, accumulator.Build().Entries.Select(entry => entry.DistinguishedName));
        var unicode = new BoundedDirectorySortAccumulator(Query, 0, 4);
        foreach (var entry in new[] { Entry("1", "é", null), Entry("2", "E", null), Entry("3", "e", null), Entry("4", "e\u0301", null) }) { unicode.Add(entry); }
        Assert.Equal(4, unicode.Build().Entries.Select(entry => entry.DistinguishedName).Distinct().Count());
    }

    [Fact]
    public async Task ExcessivePageAndMissingCapFailBeforeAnyDirectoryConnection()
    {
        var reader = new LdapDirectoryReader(NullLogger<LdapDirectoryReader>.Instance);
        foreach (var query in new[] { Query, Query with { MaximumSortedPageEntries = null } })
        {
            var result = await reader.SearchPageAsync(query, 100, 1, CancellationToken.None);
            Assert.Equal(ErrorCode.InvalidRequest, result.Error!.Code);
        }
    }

    [Fact]
    public async Task CancelledSearchNeverConnects()
    {
        var reader = new LdapDirectoryReader(NullLogger<LdapDirectoryReader>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.SearchPageAsync(Query, 0, 10, new CancellationToken(true)));
    }

    private static string Dn(string value) => $"CN={value},DC=example,DC=test";
    private static DirectoryEntryData Entry(string id, string? name, string? account) => new(Dn(id),
        new Dictionary<string, IReadOnlyList<string>> { ["name"] = name is null ? [] : [name], ["sAMAccountName"] = account is null ? [] : [account] });
}
