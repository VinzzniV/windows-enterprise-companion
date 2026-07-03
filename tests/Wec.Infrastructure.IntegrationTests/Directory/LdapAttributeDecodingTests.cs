using System.Text;
using Wec.Infrastructure.Directory;

namespace Wec.Infrastructure.IntegrationTests.Directory;

public sealed class LdapAttributeDecodingTests
{
    /// <summary>
    /// Regression: S.DS.P returns every attribute value received off the wire
    /// as byte[]. Encoding them all as Base64 turned the defaultNamingContext
    /// into "REM9a2F1dGgsREM9bG9jYWw=" and every follow-up search failed with
    /// BAD_NAME. Directory strings must decode as UTF-8.
    /// </summary>
    [Fact]
    public void Utf8StringBytes_DecodeToTheString()
    {
        byte[] namingContext = Encoding.UTF8.GetBytes("DC=kauth,DC=local");

        Assert.Equal("DC=kauth,DC=local", LdapDirectoryReader.DecodeAttributeValue(namingContext));
    }

    [Fact]
    public void UmlautsAndUnicode_SurviveDecoding()
    {
        byte[] value = Encoding.UTF8.GetBytes("OU=Bürokommunikation,DC=kauth,DC=local");

        Assert.Equal("OU=Bürokommunikation,DC=kauth,DC=local", LdapDirectoryReader.DecodeAttributeValue(value));
    }

    [Fact]
    public void BinarySidBytes_StayBase64()
    {
        // Typical objectSid prefix: revision 1, five sub-authorities, NT authority
        byte[] sid = [0x01, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05, 0x15, 0x00, 0x00, 0x00];

        Assert.Equal(Convert.ToBase64String(sid), LdapDirectoryReader.DecodeAttributeValue(sid));
    }

    [Fact]
    public void NonUtf8Bytes_StayBase64()
    {
        byte[] invalidUtf8 = [0xC3, 0x28, 0xFF];

        Assert.Equal(Convert.ToBase64String(invalidUtf8), LdapDirectoryReader.DecodeAttributeValue(invalidUtf8));
    }

    [Fact]
    public void PlainStringValues_PassThrough()
    {
        Assert.Equal("kauth.local", LdapDirectoryReader.DecodeAttributeValue("kauth.local"));
    }
}
