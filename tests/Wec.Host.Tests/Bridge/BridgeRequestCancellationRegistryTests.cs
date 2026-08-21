using Wec.Host.Bridge;

namespace Wec.Host.Tests.Bridge;

public sealed class BridgeRequestCancellationRegistryTests
{
    [Fact]
    public void CancelIsCorrelatedIdempotentAndCompletionDisposesTheRegistration()
    {
        using var registry = new BridgeRequestCancellationRegistry();
        Assert.True(registry.TryRegister("request-1", out CancellationTokenSource first));
        Assert.True(registry.TryRegister("request-2", out CancellationTokenSource second));

        Assert.True(registry.Cancel("request-1"));
        Assert.True(first.IsCancellationRequested);
        Assert.False(second.IsCancellationRequested);
        Assert.True(registry.Cancel("request-1"));
        Assert.False(registry.Cancel("missing"));

        registry.Complete("request-1");
        Assert.False(registry.Cancel("request-1"));
        registry.Complete("request-2");
    }

    [Fact]
    public void DuplicateRequestIdDoesNotReplaceTheRunningCancellationSource()
    {
        using var registry = new BridgeRequestCancellationRegistry();
        Assert.True(registry.TryRegister("same", out CancellationTokenSource original));
        Assert.False(registry.TryRegister("same", out _));

        Assert.True(registry.Cancel("same"));
        Assert.True(original.IsCancellationRequested);
    }
}
