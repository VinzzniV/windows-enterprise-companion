using Wec.Core.Contracts;
using Wec.Modules.ActiveDirectory.Application;

namespace Wec.Modules.ActiveDirectory.Tests.Application;

public sealed class AdFiltersTests
{
    [Fact]
    public void DirectoryUsers_EscapesSearchAndAppliesAllowlistedFilters()
    {
        string filter = AdFilters.DirectoryUsers(
            " Alex*(Ops) ",
            DirectoryUserAccountStateFilter.Enabled,
            " Research*(EU) ");

        Assert.Equal(
            "(&(objectCategory=person)(objectClass=user)"
            + "(!(userAccountControl:1.2.840.113556.1.4.803:=2))"
            + "(department=Research\\2a\\28EU\\29)"
            + "(|(displayName=*Alex\\2a\\28Ops\\29*)(sAMAccountName=*Alex\\2a\\28Ops\\29*)"
            + "(userPrincipalName=*Alex\\2a\\28Ops\\29*)(employeeID=*Alex\\2a\\28Ops\\29*)))",
            filter);
    }

    [Fact]
    public void UserByObjectGuid_UsesTheDirectoryBinaryGuidEncoding()
    {
        var objectId = new Guid("00112233-4455-6677-8899-aabbccddeeff");

        string filter = AdFilters.UserByObjectGuid(objectId);

        Assert.Equal(
            "(&(objectCategory=person)(objectClass=user)(objectGUID="
            + "\\33\\22\\11\\00\\55\\44\\77\\66\\88\\99\\aa\\bb\\cc\\dd\\ee\\ff))",
            filter);
    }

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
    public void DirectMembersOfGroup_UsesEscapedBacklinkAndIdentitySearch()
    {
        string filter = AdFilters.WithDirectoryIdentitySearch(
            AdFilters.DirectMembersOfGroup("CN=Admins (Tier 0),DC=x"),
            "ops*(admin)");

        Assert.Contains(@"(memberOf=CN=Admins \28Tier 0\29,DC=x)", filter, StringComparison.Ordinal);
        Assert.Contains(@"(cn=*ops\2a\28admin\29*)", filter, StringComparison.Ordinal);
        Assert.Contains(@"(sAMAccountName=*ops\2a\28admin\29*)", filter, StringComparison.Ordinal);
    }

    [Fact]
    public void InactiveUsers_ExcludesDisabledAccountsAndUsesInvariantFileTime()
    {
        string filter = AdFilters.InactiveUsers(133_800_000_000_000_000);

        Assert.Contains("(lastLogonTimestamp<=133800000000000000)", filter, StringComparison.Ordinal);
        Assert.Contains("(!(userAccountControl:1.2.840.113556.1.4.803:=2))", filter, StringComparison.Ordinal);
    }
}
