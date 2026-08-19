using Microsoft.Extensions.Logging.Abstractions;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Infrastructure.Network;

namespace Wec.Infrastructure.IntegrationTests.Network;

public sealed class SystemNetworkInfoProviderTests
{
    [Fact]
    public void ActiveAdapters_ExposeAtMostOneWindowsPreferredIpv4Route()
    {
        var provider = new SystemNetworkInfoProvider(NullLogger<SystemNetworkInfoProvider>.Instance);

        Result<IReadOnlyList<NetworkAdapterInfo>> result = provider.GetActiveAdapters();

        Assert.True(result.IsSuccess);
        List<NetworkAdapterInfo> preferred = result.Value
            .Where(adapter => adapter.IsPreferredRoute == true)
            .ToList();
        Assert.True(preferred.Count <= 1);
        Assert.All(preferred, adapter => Assert.NotNull(adapter.InterfaceIndex));
    }
}
