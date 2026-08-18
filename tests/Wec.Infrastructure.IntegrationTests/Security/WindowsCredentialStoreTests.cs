using Wec.Core.Contracts;
using Wec.Infrastructure.Security;

namespace Wec.Infrastructure.IntegrationTests.Security;

public sealed class WindowsCredentialStoreTests
{
    [Fact]
    public void IdentityRoundTrip_PreservesDomainAndUserName()
    {
        string identity = WindowsCredentialStore.JoinIdentity(
            new StoredServiceCredential("svc-wec", "CORP", "not-used"));

        (string userName, string? domain) = WindowsCredentialStore.SplitIdentity(identity);

        Assert.Equal("CORP\\svc-wec", identity);
        Assert.Equal("svc-wec", userName);
        Assert.Equal("CORP", domain);
    }

    [Fact]
    public void IdentityRoundTrip_PreservesUpnWithoutInventingDomain()
    {
        (string userName, string? domain) = WindowsCredentialStore.SplitIdentity("reader@example.test");

        Assert.Equal("reader@example.test", userName);
        Assert.Null(domain);
    }
}
