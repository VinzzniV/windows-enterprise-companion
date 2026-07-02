using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Wec.Host.Bridge;
using Wec.Host.Options;

namespace Wec.Host;

internal sealed class MainWindow : Form
{
    // Temporary until M1 step 5 wires the real frontend; exercises the bridge round trip
    private const string PlaceholderPage = """
        <!doctype html>
        <html>
        <head><meta charset="utf-8"><title>WEC</title></head>
        <body style="font-family: system-ui; display: grid; place-items: center; height: 100vh; margin: 0; background: #0f172a; color: #e2e8f0;">
          <div style="text-align: center;">
            <h1>Windows Enterprise Companion</h1>
            <p>Host bootstrap OK — frontend assets not wired yet (M1 step 5).</p>
            <p id="bridge-status">Bridge: waiting for ping response …</p>
          </div>
          <script>
            const bridge = window.chrome?.webview;
            if (bridge) {
              bridge.addEventListener('message', (event) => {
                const response = event.data;
                if (response.id === 'placeholder-ping' && response.success) {
                  document.getElementById('bridge-status').textContent =
                    `Bridge: round trip OK — ${response.data.message} @ ${response.data.timestamp}`;
                  bridge.postMessage({ id: 'placeholder-ping-confirmed', module: 'system', action: 'ping', payload: null });
                }
              });
              bridge.postMessage({ id: 'placeholder-ping', module: 'system', action: 'ping', payload: null });
            }
          </script>
        </body>
        </html>
        """;

    private readonly WebView2 _webView;
    private readonly WebViewBridge _bridge;
    private readonly WebViewOptions _webViewOptions;
    private readonly ILogger<MainWindow> _logger;

    public MainWindow(WebViewBridge bridge, IOptions<WebViewOptions> webViewOptions, ILogger<MainWindow> logger)
    {
        _bridge = bridge;
        _webViewOptions = webViewOptions.Value;
        _logger = logger;

        Text = "Windows Enterprise Companion";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1280, 800);

        _webView = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_webView);

        Load += HandleLoad;
    }

    private async void HandleLoad(object? sender, EventArgs e)
    {
        try
        {
            string userDataDirectory = Environment.ExpandEnvironmentVariables(_webViewOptions.UserDataDirectory);
            CoreWebView2Environment environment =
                await CoreWebView2Environment.CreateAsync(userDataFolder: userDataDirectory);
            await _webView.EnsureCoreWebView2Async(environment);

            _bridge.Attach(_webView.CoreWebView2);
            _webView.CoreWebView2.NavigateToString(PlaceholderPage);
            _logger.LogInformation(
                "WebView2 initialized, runtime version {RuntimeVersion}",
                environment.BrowserVersionString);
        }
        catch (WebView2RuntimeNotFoundException exception)
        {
            _logger.LogCritical(exception, "WebView2 Evergreen Runtime is not installed");
            MessageBox.Show(
                this,
                "The WebView2 runtime is missing. Install the Microsoft Edge WebView2 Evergreen Runtime and restart the app.",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
        }
    }
}
