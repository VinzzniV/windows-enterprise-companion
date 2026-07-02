using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Wec.Core.Messaging;
using Wec.Core.Results;

namespace Wec.Host.Bridge;

internal sealed class HandlerRegistration
{
    private static readonly MethodInfo OpenInvokeMethod = typeof(HandlerRegistration)
        .GetMethod(nameof(InvokeCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly IActionHandler _handler;
    private readonly MethodInfo _closedInvokeMethod;

    private HandlerRegistration(IActionHandler handler, MethodInfo closedInvokeMethod)
    {
        _handler = handler;
        _closedInvokeMethod = closedInvokeMethod;
    }

    public static HandlerRegistration Create(IActionHandler handler)
    {
        Type? closedHandlerInterface = handler.GetType().GetInterfaces().SingleOrDefault(
            candidate => candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IActionHandler<,>));

        if (closedHandlerInterface is null)
        {
            throw new InvalidOperationException(
                $"{handler.GetType().Name} must implement IActionHandler<TPayload, TResult> exactly once.");
        }

        MethodInfo closedInvokeMethod =
            OpenInvokeMethod.MakeGenericMethod(closedHandlerInterface.GetGenericArguments());
        return new HandlerRegistration(handler, closedInvokeMethod);
    }

    public async Task<BridgeResponse> InvokeAsync(BridgeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await (Task<BridgeResponse>)_closedInvokeMethod
                .Invoke(null, [_handler, request, cancellationToken])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw; // unreachable; satisfies definite return analysis
        }
    }

    private static async Task<BridgeResponse> InvokeCoreAsync<TPayload, TResult>(
        IActionHandler handler,
        BridgeRequest request,
        CancellationToken cancellationToken)
    {
        var typedHandler = (IActionHandler<TPayload, TResult>)handler;
        TPayload payload = DeserializePayload<TPayload>(request.Payload);

        Result<TResult> result = await typedHandler.HandleAsync(payload, cancellationToken);

        return result.IsSuccess
            ? BridgeResponse.ForSuccess(request.Id, result.Value)
            : BridgeResponse.ForFailure(request.Id, result.Error!);
    }

    private static TPayload DeserializePayload<TPayload>(JsonElement? payload)
    {
        JsonElement effectivePayload = payload is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } element
            ? element
            : JsonDocument.Parse("{}").RootElement;

        return effectivePayload.Deserialize<TPayload>(BridgeJson.Options)
            ?? throw new JsonException($"Payload of type {typeof(TPayload).Name} deserialized to null.");
    }
}
