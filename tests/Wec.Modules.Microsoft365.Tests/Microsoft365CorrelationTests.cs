using Wec.Core.Microsoft365;
using Wec.Modules.Microsoft365.Application;

namespace Wec.Modules.Microsoft365.Tests;

public sealed class Microsoft365CorrelationTests
{
    private const string Sid = "S-1-5-21-1-2-3-1001";
    private static Microsoft365User User(string? sid, string? upn) => new("11111111-1111-1111-1111-111111111111", "Same Name", upn,
        null, null, null, null, null, null, null, sid, null, null);
    private static Microsoft365Device Device(string? id) => new("object", id, "PC-1", null, null, null, null, null);
    private static Microsoft365ManagedDevice Managed(string? id) => new("managed", "PC-1", null, null,
        null, null, null, null, null, null, null, null, null, id);

    [Fact]
    public void SidHasPriorityOverMutableUpnAndDuplicateSidIsAmbiguous()
    {
        var matching = User(Sid, "old@example.test");
        var sameName = User("different", "new@example.test");
        Assert.Same(matching, Microsoft365CorrelationPolicy.User([matching, sameName], Sid, "new@example.test").User);
        Assert.Equal("Ambiguous", Microsoft365CorrelationPolicy.User([matching, matching], Sid, null).State);
    }

    [Fact]
    public void UpnIsOnlyCandidateAndDisplayNameNeverMatches()
    {
        Assert.Equal("Candidate", Microsoft365CorrelationPolicy.User([User(null, "a@example.test")], null, "A@example.test").State);
        Assert.Null(Microsoft365CorrelationPolicy.User([User(null, null)], null, "Same Name").User);
        Assert.Null(Microsoft365CorrelationPolicy.User([User(null, null)], null, null).User);
    }

    [Fact]
    public void ConflictingSidAndMissingObjectIdsNeverProduceAnIdentityLink()
    {
        Assert.Equal("Conflict", Microsoft365CorrelationPolicy.User([User("S-1-5-21-1-2-3-9999", "a@example.test")], Sid, "a@example.test").State);
        Assert.Null(Microsoft365CorrelationPolicy.User([User(Sid, null) with { Id = null }], Sid, null).User);
        Assert.Null(Microsoft365CorrelationPolicy.User([User("invalid", null)], "invalid", null).User);
    }

    [Fact]
    public void EntraAndIntuneJoinOnlyByDeviceIdNotObjectIdOrName()
    {
        string id = Guid.NewGuid().ToString("D");
        var device = Device(id);
        var managed = Managed(id.ToUpperInvariant());
        Assert.Same(managed, Microsoft365CorrelationPolicy.Device([device], [managed], null, id).ManagedDevice);
        Assert.Equal("Matched", Microsoft365CorrelationPolicy.Device([device], [managed], null, id).State);
        Assert.Equal("Candidate", Microsoft365CorrelationPolicy.Device([device], [managed], "PC-1", null).State);
        Assert.Null(Microsoft365CorrelationPolicy.Device([device], [Managed("object")], null, id).ManagedDevice);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("invalid")]
    public void MissingAndPlaceholderIdsNeverJoin(string? id)
    {
        Assert.Null(Microsoft365CorrelationPolicy.Device([Device(id)], [Managed(id)], "PC-1", null).ManagedDevice);
    }

    [Fact]
    public void DuplicateEntraOrIntuneIdsRemainAmbiguous()
    {
        string id = Guid.NewGuid().ToString("D");
        Assert.Null(Microsoft365CorrelationPolicy.Device([Device(id), Device(id)], [Managed(id)], null, id).Device);
        Assert.Null(Microsoft365CorrelationPolicy.Device([Device(id)], [Managed(id), Managed(id)], null, id).ManagedDevice);
    }
}
