using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wec.Core.Messaging;
using Wec.Core.Results;

namespace Wec.Host.Bridge;

internal sealed partial class ActionDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBridgeExecutionTimeoutPolicy _timeoutPolicy;
    private readonly ILogger<ActionDispatcher> _logger;

    public ActionDispatcher(
        IServiceScopeFactory scopeFactory,
        IBridgeExecutionTimeoutPolicy timeoutPolicy,
        ILogger<ActionDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _timeoutPolicy = timeoutPolicy;
        _logger = logger;
    }

    public async Task<BridgeResponse> DispatchAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        using IDisposable? correlationScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = request.Id,
            ["Module"] = request.Module,
            ["Action"] = request.Action,
        });

        // Handlers are resolved per request so they can depend on scoped services
        // (DbContext etc.); uniqueness of (module, action) is validated at startup
        using IServiceScope serviceScope = _scopeFactory.CreateScope();
        IActionHandler? handler = serviceScope.ServiceProvider
            .GetServices<IActionHandler>()
            .FirstOrDefault(candidate => candidate.Module == request.Module && candidate.Action == request.Action);

        if (handler is null)
        {
            _logger.LogWarning("No handler registered for {Module}/{Action}", request.Module, request.Action);
            return BridgeResponse.ForFailure(request.Id, new Error(
                ErrorCode.UnknownAction,
                $"No handler registered for '{request.Module}/{request.Action}'."));
        }

        TimeSpan timeout = _timeoutPolicy.Resolve(request);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            BridgeResponse response = await HandlerRegistration.Create(handler)
                .InvokeAsync(request, timeoutSource.Token);
            LogRequestHandled(response.Success);
            return response;
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Bridge request payload could not be deserialized");
            return BridgeResponse.ForFailure(request.Id, new Error(
                ErrorCode.InvalidRequest,
                "The request payload does not match the expected shape."));
        }
        catch (OperationCanceledException) when (
            timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            LogRequestTimedOut(timeout.TotalSeconds);
            return BridgeResponse.ForFailure(request.Id, new Error(
                ErrorCode.ConnectionTimeout,
                $"The operation did not finish within {timeout.TotalSeconds:0} seconds."));
        }
        catch (OperationCanceledException)
        {
            return BridgeResponse.ForFailure(request.Id, new Error(
                ErrorCode.ServiceUnavailable,
                "The operation was cancelled before it completed."));
        }
        catch (Exception exception)
        {
            // Exceptions are bugs (ADR 0002); map to a generic error without leaking internals
            _logger.LogError(exception, "Unhandled exception in action handler");
            return BridgeResponse.ForFailure(request.Id, new Error(
                ErrorCode.InternalError,
                "An internal error occurred. See the application log for details."));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Bridge request handled, success: {Success}")]
    private partial void LogRequestHandled(bool success);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Bridge request exceeded its {TimeoutSeconds}-second execution limit")]
    private partial void LogRequestTimedOut(double timeoutSeconds);
}
