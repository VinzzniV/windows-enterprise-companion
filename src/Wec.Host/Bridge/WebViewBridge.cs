using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Wec.Core.Messaging;
using Wec.Core.Results;

namespace Wec.Host.Bridge;

internal sealed class WebViewBridge
{
    private readonly ActionDispatcher _dispatcher;
    private readonly BridgeRequestCancellationRegistry _cancellations;
    private readonly ILogger<WebViewBridge> _logger;
    private string _trustedOrigin = "https://app.wec";

    public WebViewBridge(
        ActionDispatcher dispatcher,
        BridgeRequestCancellationRegistry cancellations,
        ILogger<WebViewBridge> logger)
    {
        _dispatcher = dispatcher;
        _cancellations = cancellations;
        _logger = logger;
    }

    public void Attach(CoreWebView2 coreWebView, string trustedOrigin = "https://app.wec")
    {
        ArgumentNullException.ThrowIfNull(coreWebView);
        _trustedOrigin = trustedOrigin;
        coreWebView.WebMessageReceived += HandleWebMessageReceived;
    }

    // async void is acceptable here: WinForms event handler with a full try/catch,
    // continuations stay on the UI thread so PostWebMessageAsJson is safe
    private async void HandleWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var coreWebView = (CoreWebView2)sender!;
        try
        {
            if (!HasTrustedOrigin(e.Source, _trustedOrigin))
            {
                _logger.LogWarning("Rejected bridge message from an untrusted origin");
                return;
            }
            if (TryParseCancellation(e.WebMessageAsJson, out string? cancelledRequestId))
            {
                _cancellations.Cancel(cancelledRequestId);
                return;
            }

            BridgeRequest? request = ParseRequest(e.WebMessageAsJson);
            if (request is null || string.IsNullOrWhiteSpace(request.Id))
            {
                _logger.LogWarning("Dropped bridge message without a correlation id");
                return;
            }

            if (!_cancellations.TryRegister(request.Id, out CancellationTokenSource requestCancellation))
            {
                coreWebView.PostWebMessageAsJson(JsonSerializer.Serialize(
                    BridgeResponse.ForFailure(request.Id, new Error(
                        ErrorCode.InvalidRequest,
                        "A request with the same correlation id is already running.")),
                    BridgeJson.Options));
                return;
            }

            BridgeResponse response;
            try
            {
                response = string.IsNullOrWhiteSpace(request.Module) || string.IsNullOrWhiteSpace(request.Action)
                    ? BridgeResponse.ForFailure(request.Id, new Error(
                        ErrorCode.InvalidRequest,
                        "Envelope fields 'module' and 'action' are required."))
                    : await _dispatcher.DispatchAsync(request, requestCancellation.Token);
            }
            finally
            {
                _cancellations.Complete(request.Id);
            }

            coreWebView.PostWebMessageAsJson(JsonSerializer.Serialize(response, BridgeJson.Options));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Bridge message handling failed");
        }
    }

    internal static bool HasTrustedOrigin(string source, string trustedOrigin) =>
        Uri.TryCreate(source, UriKind.Absolute, out Uri? actual)
        && Uri.TryCreate(trustedOrigin, UriKind.Absolute, out Uri? expected)
        && actual.Scheme == expected.Scheme && actual.Host == expected.Host
        && actual.Port == expected.Port && actual.UserInfo.Length == 0;

    private static bool TryParseCancellation(string webMessageJson, out string requestId)
    {
        requestId = string.Empty;
        try
        {
            using JsonDocument document = JsonDocument.Parse(webMessageJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out JsonElement type)
                || !string.Equals(type.GetString(), "cancel", StringComparison.Ordinal)
                || !root.TryGetProperty("id", out JsonElement id)
                || string.IsNullOrWhiteSpace(id.GetString()))
            {
                return false;
            }

            requestId = id.GetString()!;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private BridgeRequest? ParseRequest(string webMessageJson)
    {
        try
        {
            return JsonSerializer.Deserialize<BridgeRequest>(webMessageJson, BridgeJson.Options);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Bridge message is not a valid request envelope");
            return null;
        }
    }
}
