using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Wec.Core.Messaging;

namespace Wec.Host.Bridge;

/// <summary>
/// Posts event envelopes to the frontend. Publish may be called from any
/// thread (batch scans run on the thread pool); PostWebMessageAsJson must run
/// on the UI thread, so the call is marshaled through the WebView's control.
/// </summary>
internal sealed partial class WebViewBridgeEventPublisher : IBridgeEventPublisher
{
    private readonly ILogger<WebViewBridgeEventPublisher> _logger;
    private volatile Control? _uiThreadControl;
    private volatile CoreWebView2? _coreWebView;

    public WebViewBridgeEventPublisher(ILogger<WebViewBridgeEventPublisher> logger)
    {
        _logger = logger;
    }

    public void Attach(Control uiThreadControl, CoreWebView2 coreWebView)
    {
        _uiThreadControl = uiThreadControl;
        _coreWebView = coreWebView;
    }

    public void Publish(BridgeEvent bridgeEvent)
    {
        Control? control = _uiThreadControl;
        CoreWebView2? coreWebView = _coreWebView;
        if (control is null || coreWebView is null || control.IsDisposed)
        {
            return;
        }

        string json = JsonSerializer.Serialize(bridgeEvent, BridgeJson.Options);
        try
        {
            control.BeginInvoke(() =>
            {
                try
                {
                    coreWebView.PostWebMessageAsJson(json);
                }
                catch (InvalidOperationException)
                {
                    // WebView torn down between the check and the post — event is lost by design
                }
            });
        }
        catch (InvalidOperationException exception)
        {
            LogEventDropped(exception, bridgeEvent.Module, bridgeEvent.EventName);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dropped bridge event {Module}/{EventName} — window is closing")]
    private partial void LogEventDropped(Exception exception, string module, string eventName);
}
