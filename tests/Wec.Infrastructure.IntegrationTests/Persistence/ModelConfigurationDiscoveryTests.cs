using Wec.Infrastructure.Persistence;
using Wec.Modules.Inventory.Persistence;

namespace Wec.Infrastructure.IntegrationTests.Persistence;

public sealed class ModelConfigurationDiscoveryTests
{
    [Fact]
    public void DetectsAssembliesWithEntityConfigurations()
    {
        Assert.True(WecDbContext.ContainsEntityTypeConfiguration(typeof(HardwareSnapshotRecord).Assembly));
    }

    [Fact]
    public void SkipsAssembliesWithoutEntityConfigurations()
    {
        Assert.False(WecDbContext.ContainsEntityTypeConfiguration(typeof(Wec.Core.Results.Error).Assembly));
    }
}
