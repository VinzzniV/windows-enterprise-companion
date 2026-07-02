using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wec.Core.Messaging;
using Wec.Core.Results;

namespace Wec.Host.Bridge;

internal sealed class ActionDispatcher
{
    private readonly Dictionary<(string Module, string Action), HandlerRegistration> _registrations = [];
    private readonly ILogger<ActionDispatcher> _logger;

    public ActionDispatcher(IEnumerable<IActionHandler> handlers, ILogger<ActionDispatcher> logger)
    {
        _logger = logger;

        foreach (IActionHandler handler in handlers)
        {
            if (!_registrations.TryAdd((handler.Module, handler.Action), HandlerRegistration.Create(handler)))
            {
                throw new InvalidOperationException(
                    $"Duplicate action handler registration for '{handler.Module}/{handler.Action}'.");
            }
        }
    }

    public async Task<BridgeResponse> DispatchAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        using IDisposable? correlationScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = request.Id,
            ["Module"] = request.Module,
            ["Action"] = request.Action,
        });

        if (!_registrations.TryGetValue((request.Module, request.Action), out HandlerRegistration? registration))
        {
            _logger.LogWarning("No handler registered for {Module}/{Action}", request.Module, request.Action);
            return BridgeResponse.ForFailure(request.Id, new Error(
                ErrorCode.UnknownAction,
                $"No handler registered for '{request.Module}/{request.Action}'."));
        }

        try
        {
            BridgeResponse response = await registration.InvokeAsync(request, cancellationToken);
            _logger.LogInformation("Bridge request handled, success: {Success}", response.Success);
            return response;
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Bridge request payload could not be deserialized");
            return BridgeResponse.ForFailure(request.Id, new Error(
                ErrorCode.InvalidRequest,
                "The request payload does not match the expected shape."));
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
}
