using Wec.Core.Targets;

namespace Wec.Core.Tests.Targets;

public sealed class HostAddressTests
{
    [Theory]
    [InlineData(" pc01.site-a.example. ", "PC01.SITE-A.EXAMPLE")]
    [InlineData("10.1.2.3", "10.1.2.3")]
    [InlineData("10.8.9.10", "10.8.9.10")]
    [InlineData("2001:0db8:0000:0000:0000:0000:0000:0001", "2001:DB8::1")]
    [InlineData("[2001:db8::1]", "2001:DB8::1")]
    public void ComparisonRetainsFullAddress(string input, string expected) =>
        Assert.Equal(expected, HostAddress.ComparisonKey(input));

    [Theory]
    [InlineData("10.1.2.3")]
    [InlineData("2001:db8::1")]
    [InlineData("")]
    public void AddressesAndEmptyValuesAreNotShortNameAliases(string input) =>
        Assert.Null(HostAddress.ShortNameAlias(input));

    [Fact]
    public void CrossDomainNamesRemainSeparateAndCannotSelectLocalExecution()
    {
        Assert.NotEqual(HostAddress.ComparisonKey("PC01.a.example"), HostAddress.ComparisonKey("PC01.b.example"));
        Assert.Equal("PC01", HostAddress.ShortNameAlias("PC01.a.example"));
        Assert.False(HostAddress.IsExactLocalName("PC01.b.example", "PC01"));
        Assert.False(HostAddress.IsExactLocalName("PC01", ""));
        Assert.True(HostAddress.IsExactLocalName(" pc01 ", "PC01"));
    }
}
