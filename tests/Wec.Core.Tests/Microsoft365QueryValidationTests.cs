using Wec.Core.Microsoft365;

namespace Wec.Core.Tests;

public sealed class Microsoft365QueryValidationTests
{
    [Fact]
    public void SidQueryAcceptsOnlyTheExactAccountSidShape()
    {
        var valid = Microsoft365QueryValidation.Normalize(new(Microsoft365Resource.UsersBySid, SecurityIdentifier: "s-1-5-21-1-2-3-001001"));
        Assert.True(valid.IsSuccess);
        Assert.Equal("S-1-5-21-1-2-3-1001", valid.Value.SecurityIdentifier);
        Assert.Null(valid.Value.ObjectId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("S-1-5-32-544")]
    [InlineData("S-1-5-21-1-2-3")]
    [InlineData("S-1-5-21-1-2-3-1001' or true")]
    [InlineData("S-1-5-21-1-2-3-4294967296")]
    public void InvalidSidCannotBecomeAGraphFilter(string? sid) =>
        Assert.True(Microsoft365QueryValidation.Normalize(new(Microsoft365Resource.UsersBySid, SecurityIdentifier: sid)).IsFailure);

    [Fact]
    public void QueryIdentifiersCannotBeMixedOrSmuggledIntoAnInventory()
    {
        const string sid = "S-1-5-21-1-2-3-1001";
        Assert.True(Microsoft365QueryValidation.Normalize(new(Microsoft365Resource.UsersBySid, Guid.NewGuid().ToString("D"), sid)).IsFailure);
        Assert.True(Microsoft365QueryValidation.Normalize(new(Microsoft365Resource.Users, SecurityIdentifier: sid)).IsFailure);
        Assert.True(Microsoft365QueryValidation.Normalize(new(Microsoft365Resource.User, Guid.NewGuid().ToString("D"), sid)).IsFailure);
    }
}
