using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wec.Core.Messaging;
using Wec.Core.Microsoft365;
using Wec.Host.Bridge;

namespace Wec.Host.Tests;

public sealed class Microsoft365BoundaryTests
{
    [Theory]
    [InlineData("https://app.wec/index.html#/microsoft365", true)]
    [InlineData("https://app.wec.evil.example/index.html", false)]
    [InlineData("https://evil.example", false)]
    [InlineData("http://app.wec", false)]
    [InlineData("https://app.wec:444/", false)]
    [InlineData("https://user@app.wec/", false)]
    public void BridgeRejectsUntrustedOrigins(string source, bool allowed) =>
        Assert.Equal(allowed, WebViewBridge.HasTrustedOrigin(source, "https://app.wec"));

    [Fact]
    public void Microsoft365HandlersResolveWithoutAuthenticationOrExternalReads()
    {
        using IHost host = Program.BuildHost([]);
        using IServiceScope scope = host.Services.CreateScope();
        Assert.Equal(5, scope.ServiceProvider.GetServices<IActionHandler>().Count(handler => handler.Module == "microsoft365"));
        Assert.False(scope.ServiceProvider.GetRequiredService<IMicrosoft365Reader>().Connection.Connected);
    }

    [Theory]
    [InlineData("connect", 310)]
    [InlineData("read", 115)]
    public void BridgeLifetimeExceedsConfiguredProviderDeadline(string action, int seconds)
    {
        using JsonDocument payload = JsonDocument.Parse("{}");
        var request = new BridgeRequest("test", "microsoft365", action, payload.RootElement);
        Assert.Equal(TimeSpan.FromSeconds(seconds), new BridgeExecutionTimeoutPolicy().Resolve(request));
    }
}
