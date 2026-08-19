using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Host.Bridge;

namespace Wec.Host.Tests.Bridge;

public sealed class ActionDispatcherTests
{
    public sealed record EchoPayload(string Text);

    public sealed record EchoResult(string Echoed);

    private sealed class EchoHandler : IActionHandler<EchoPayload, EchoResult>
    {
        public string Module => "test";

        public string Action => "echo";

        public Task<Result<EchoResult>> HandleAsync(EchoPayload payload, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new EchoResult(payload.Text)));
    }

    private sealed class FailingHandler : IActionHandler<EchoPayload, EchoResult>
    {
        public string Module => "test";

        public string Action => "fail";

        public Task<Result<EchoResult>> HandleAsync(EchoPayload payload, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Failure<EchoResult>(Error.NotFound("nothing here")));
    }

    private sealed class ThrowingHandler : IActionHandler<EchoPayload, EchoResult>
    {
        public string Module => "test";

        public string Action => "throw";

        public Task<Result<EchoResult>> HandleAsync(EchoPayload payload, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("bug with secret internals");
    }

    private sealed class BlockingHandler : IActionHandler<EchoPayload, EchoResult>
    {
        public string Module => "test";

        public string Action => "block";

        public async Task<Result<EchoResult>> HandleAsync(
            EchoPayload payload,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Result.Success(new EchoResult(payload.Text));
        }
    }

    private sealed class TestTimeoutPolicy(TimeSpan timeout) : IBridgeExecutionTimeoutPolicy
    {
        public TimeSpan Resolve(BridgeRequest request) => timeout;
    }

    private static ActionDispatcher CreateDispatcher(TimeSpan? timeout = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<IActionHandler, EchoHandler>();
        services.AddScoped<IActionHandler, FailingHandler>();
        services.AddScoped<IActionHandler, ThrowingHandler>();
        services.AddScoped<IActionHandler, BlockingHandler>();
        ServiceProvider provider = services.BuildServiceProvider();
        return new ActionDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new TestTimeoutPolicy(timeout ?? TimeSpan.FromSeconds(5)),
            NullLogger<ActionDispatcher>.Instance);
    }

    private static BridgeRequest Request(string action, object? payload = null) => new(
        Guid.NewGuid().ToString(),
        "test",
        action,
        payload is null ? null : JsonSerializer.SerializeToElement(payload, BridgeJson.Options));

    [Fact]
    public async Task SuccessfulHandler_ReturnsSuccessEnvelopeWithCorrelatedId()
    {
        BridgeRequest request = Request("echo", new EchoPayload("hello"));

        BridgeResponse response = await CreateDispatcher().DispatchAsync(request, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(request.Id, response.Id);
        Assert.Equal("hello", Assert.IsType<EchoResult>(response.Data).Echoed);
    }

    [Fact]
    public async Task UnknownAction_ReturnsUnknownActionError()
    {
        BridgeResponse response = await CreateDispatcher()
            .DispatchAsync(Request("doesNotExist"), CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(ErrorCode.UnknownAction, response.Error!.Code);
    }

    [Fact]
    public async Task FailingHandler_MapsResultErrorIntoTheEnvelope()
    {
        BridgeResponse response = await CreateDispatcher()
            .DispatchAsync(Request("fail", new EchoPayload("x")), CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(ErrorCode.NotFound, response.Error!.Code);
    }

    [Fact]
    public async Task MalformedPayload_ReturnsInvalidRequestInsteadOfCrashing()
    {
        // Payload shape mismatch: Text expects a string, an object cannot convert
        var request = new BridgeRequest(
            "id-1",
            "test",
            "echo",
            JsonSerializer.SerializeToElement(new { text = new { nested = true } }));

        BridgeResponse response = await CreateDispatcher().DispatchAsync(request, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(ErrorCode.InvalidRequest, response.Error!.Code);
    }

    [Fact]
    public async Task ThrowingHandler_ReturnsGenericInternalErrorWithoutLeakingDetails()
    {
        BridgeResponse response = await CreateDispatcher()
            .DispatchAsync(Request("throw", new EchoPayload("x")), CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(ErrorCode.InternalError, response.Error!.Code);
        // The exception message must never cross the bridge (ADR 0002/0003)
        Assert.DoesNotContain("secret internals", response.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(response.Error.Details);
    }

    [Fact]
    public async Task ExecutionLimit_CancelsHandlerAndReturnsTypedTimeout()
    {
        BridgeResponse response = await CreateDispatcher(TimeSpan.FromMilliseconds(20))
            .DispatchAsync(Request("block", new EchoPayload("x")), CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(ErrorCode.ConnectionTimeout, response.Error!.Code);
        Assert.DoesNotContain("TaskCanceledException", response.Error.Message, StringComparison.Ordinal);
    }
}
