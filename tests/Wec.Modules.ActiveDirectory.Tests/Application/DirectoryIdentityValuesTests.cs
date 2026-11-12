using System.Security.Principal;
using Wec.Core.Abstractions;
using Wec.Modules.ActiveDirectory.Application;

namespace Wec.Modules.ActiveDirectory.Tests.Application;

public sealed class DirectoryIdentityValuesTests
{
    [Fact]
    public void DecodesGuidAndSidWithoutUsingNames()
    {
        Guid id = Guid.NewGuid();
        var sid = new SecurityIdentifier("S-1-5-21-100-200-300-1200");
        byte[] sidBytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(sidBytes, 0);
        var entry = Entry(Convert.ToBase64String(id.ToByteArray()), Convert.ToBase64String(sidBytes));

        Assert.Equal(id, DirectoryIdentityValues.ObjectId(entry));
        Assert.Equal(sid.Value, DirectoryIdentityValues.SecurityIdentifier(entry));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("AQID")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==")]
    public void MissingMalformedAndZeroIdentitiesRemainUnknown(string? value)
    {
        DirectoryEntryData entry = Entry(value, value);
        Assert.Null(DirectoryIdentityValues.ObjectId(entry));
        Assert.Null(DirectoryIdentityValues.SecurityIdentifier(entry));
    }

    private static DirectoryEntryData Entry(string? guid, string? sid) => new(
        "CN=SameName,DC=example,DC=test",
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["objectGUID"] = guid is null ? [] : [guid],
            ["objectSid"] = sid is null ? [] : [sid],
        });
}
