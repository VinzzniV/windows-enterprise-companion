using Wec.Modules.ActiveDirectory.Application;

namespace Wec.Modules.ActiveDirectory.Tests.Application;

public sealed class AdFiltersTests
{
    [Theory]
    [InlineData(@"plain", @"plain")]
    [InlineData(@"CN=Admins (Tier 0)", @"CN=Admins \28Tier 0\29")]
    [InlineData(@"star*back\slash", @"star\2aback\5cslash")]
    public void EscapeFilterValue_EscapesRfc4515Characters(string input, string expected) =>
        Assert.Equal(expected, AdFilters.EscapeFilterValue(input));

    [Fact]
    public void DisabledDirectMembersOfGroups_CombinesEscapedMemberOfClauses()
    {
        string filter = AdFilters.DisabledDirectMembersOfGroups(
            ["CN=Domain Admins,DC=x", "CN=A (B),DC=x"]);

        Assert.Contains("(memberOf=CN=Domain Admins,DC=x)", filter, StringComparison.Ordinal);
        Assert.Contains(@"(memberOf=CN=A \28B\29,DC=x)", filter, StringComparison.Ordinal);
        Assert.StartsWith("(&(objectCategory=person)(objectClass=user)", filter, StringComparison.Ordinal);
        Assert.Contains(":=2)", filter, StringComparison.Ordinal);
    }

    [Fact]
    public void InactiveUsers_ExcludesDisabledAccountsAndUsesInvariantFileTime()
    {
        string filter = AdFilters.InactiveUsers(133_800_000_000_000_000);

        Assert.Contains("(lastLogonTimestamp<=133800000000000000)", filter, StringComparison.Ordinal);
        Assert.Contains("(!(userAccountControl:1.2.840.113556.1.4.803:=2))", filter, StringComparison.Ordinal);
    }
}
