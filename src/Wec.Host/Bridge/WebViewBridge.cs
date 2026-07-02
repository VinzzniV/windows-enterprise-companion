using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Wec.Core.Messaging;
using Wec.Core.Results;

namespace Wec.Host.Bridge;

internal sealed class WebViewBridge
{
    private readonly ActionDispatcher _dispatcher;
    private readonly ILogger<WebViewBridge> _logger;

    public WebViewBridge(ActionDispatcher dispatcher, ILogger<WebViewBridge> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public void Attach(CoreWebView2 coreWebView)
    {
        ArgumentNullException.ThrowIfNull(coreWebView);
        coreWebView.WebMessageReceived += HandleWebMessageReceived;
    }

    // async void is acceptable here: WinForms event handler with a full try/catch,
    // continuations stay on the UI thread so PostWebMessageAsJson is safe
    private async void HandleWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var coreWebView = (CoreWebView2)sender!;
        try
        {
            BridgeRequest? request = ParseRequest(e.WebMessageAsJson);
            if (request is null || string.IsNullOrWhiteSpace(request.Id))
            {
                _logger.LogWarning("Dropped bridge message without a correlation id");
                return;
            }

            BridgeResponse response =
                string.IsNullOrWhiteSpace(request.Module) || string.IsNullOrWhiteSpace(request.Action)
                    ? BridgeResponse.ForFailure(request.Id, new Error(
                        ErrorCode.InvalidRequest,
                        "Envelope fields 'module' and 'action' are required."))
                    : await _dispatcher.DispatchAsync(request, CancellationToken.None);

            coreWebView.PostWebMessageAsJson(JsonSerializer.Serialize(response, BridgeJson.Options));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Bridge message handling failed");
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
