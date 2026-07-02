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
    public void GetBytes_DecodesBase64AndRejectsNonBase64()
    {
        byte[] payload = [1, 5, 0, 0, 0, 42];
        var entry = new DirectoryEntryData("CN=Test", new Dictionary<string, IReadOnlyList<string>>
        {
            ["objectSid"] = [Convert.ToBase64String(payload)],
            ["description"] = ["definitely not base64!!"],
        });

        Assert.Equal(payload, entry.GetBytes("objectSid"));
        Assert.Null(entry.GetBytes("description"));
        Assert.Null(entry.GetBytes("absent"));
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
