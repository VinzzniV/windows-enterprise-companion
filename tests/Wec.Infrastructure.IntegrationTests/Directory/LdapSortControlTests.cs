using System.DirectoryServices.Protocols;
using Wec.Infrastructure.Directory;

namespace Wec.Infrastructure.IntegrationTests.Directory;

public sealed class LdapSortControlTests
{
    [Theory]
    [InlineData("displayName", false)]
    [InlineData("sAMAccountName", true)]
    [InlineData("department", false)]
    [InlineData("whenCreated", true)]
    [InlineData("lastLogonTimestamp", false)]
    public void SortControl_UsesExactlyOneAdSupportedKey(string attribute, bool descending)
    {
        SortRequestControl control = LdapDirectoryReader.CreateSortControl(attribute, descending);

        SortKey key = Assert.Single(control.SortKeys);
        Assert.Equal(attribute, key.AttributeName);
        Assert.Equal(descending, key.ReverseOrder);
        Assert.Null(key.MatchingRule);
        Assert.True(control.IsCritical);
        Assert.NotEmpty(control.GetValue());
    }
}
