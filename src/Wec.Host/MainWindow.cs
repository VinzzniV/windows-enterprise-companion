using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Wec.Host.Bridge;
using Wec.Host.Options;

namespace Wec.Host;

internal sealed partial class MainWindow : Form
{
    private const string VirtualHostName = "app.wec";

    private const string MissingAssetsPage = """
        <!doctype html>
        <html>
        <head><meta charset="utf-8"><title>WEC</title></head>
        <body style="font-family: system-ui; display: grid; place-items: center; height: 100vh; margin: 0; background: #0f172a; color: #e2e8f0;">
          <div style="text-align: center;">
            <h1>Windows Enterprise Companion</h1>
            <p>Frontend assets are missing. Run <code>npm run build</code> in <code>frontend/</code> and rebuild,
               or enable <code>Wec:Frontend:UseDevServer</code>.</p>
          </div>
        </body>
        </html>
        """;

    private readonly WebView2 _webView;
    private readonly WebViewBridge _bridge;
    private readonly WebViewBridgeEventPublisher _eventPublisher;
    private readonly WebViewOptions _webViewOptions;
    private readonly FrontendOptions _frontendOptions;
    private readonly ILogger<MainWindow> _logger;

    public MainWindow(
        WebViewBridge bridge,
        WebViewBridgeEventPublisher eventPublisher,
        IOptions<WebViewOptions> webViewOptions,
        IOptions<FrontendOptions> frontendOptions,
        ILogger<MainWindow> logger)
    {
        _bridge = bridge;
        _eventPublisher = eventPublisher;
        _webViewOptions = webViewOptions.Value;
        _frontendOptions = frontendOptions.Value;
        _logger = logger;

        Text = "Windows Enterprise Companion";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1280, 800);
        // Match the UI's slate-950 so neither the form nor the WebView flashes
        // white before the frontend has painted
        BackColor = Color.FromArgb(2, 6, 23);

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.FromArgb(2, 6, 23),
        };
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
            _eventPublisher.Attach(this, _webView.CoreWebView2);
            NavigateToFrontend();
            LogWebViewInitialized(environment.BrowserVersionString);
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

    private void NavigateToFrontend()
    {
        if (_frontendOptions.UseDevServer)
        {
            LogLoadingFromDevServer(_frontendOptions.DevServerUrl);
            _webView.CoreWebView2.Navigate(_frontendOptions.DevServerUrl);
            return;
        }

        string wwwrootDirectory = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (File.Exists(Path.Combine(wwwrootDirectory, "index.html")))
        {
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHostName,
                wwwrootDirectory,
                CoreWebView2HostResourceAccessKind.Allow);
            LogLoadingFromLocalAssets(wwwrootDirectory);
            _webView.CoreWebView2.Navigate($"https://{VirtualHostName}/index.html");
        }
        else
        {
            _logger.LogWarning("No frontend assets found in {WwwrootDirectory}", wwwrootDirectory);
            _webView.CoreWebView2.NavigateToString(MissingAssetsPage);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "WebView2 initialized, runtime version {RuntimeVersion}")]
    private partial void LogWebViewInitialized(string runtimeVersion);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loading frontend from dev server {DevServerUrl}")]
    private partial void LogLoadingFromDevServer(string devServerUrl);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loading frontend from local assets in {WwwrootDirectory}")]
    private partial void LogLoadingFromLocalAssets(string wwwrootDirectory);
}
