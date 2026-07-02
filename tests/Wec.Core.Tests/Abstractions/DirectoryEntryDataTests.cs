using Wec.Core.Abstractions;

namespace Wec.Core.Tests.Abstractions;

public sealed class DirectoryEntryDataTests
{
    [Fact]
    public void AttributeLookup_IsCaseInsensitive()
    {
        var entry = new DirectoryEntryData("CN=Test", new Dictionary<string, IReadOnlyList<string>>
        {
            ["dNSHostName"] = ["dc01.corp.example.com"],
        });

        Assert.Equal("dc01.corp.example.com", entry.GetFirstValue("dnshostname"));
        Assert.Equal(["dc01.corp.example.com"], entry.GetValues("DNSHOSTNAME"));
    }

    [Fact]
    public void MissingAttribute_YieldsEmptyValuesAndNulls()
    {
        var entry = new DirectoryEntryData("CN=Test", new Dictionary<string, IReadOnlyList<string>>());

        Assert.Empty(entry.GetValues("absent"));
        Assert.Null(entry.GetFirstValue("absent"));
        Assert.Null(entry.GetLong("absent"));
    }

    [Fact]
    public void GetLong_ParsesNumericAttributeValues()
    {
        var entry = new DirectoryEntryData("CN=Test", new Dictionary<string, IReadOnlyList<string>>
        {
            ["userAccountControl"] = ["66048"],
            ["description"] = ["not a number"],
        });

        Assert.Equal(66048, entry.GetLong("userAccountControl"));
        Assert.Null(entry.GetLong("description"));
    }
}
