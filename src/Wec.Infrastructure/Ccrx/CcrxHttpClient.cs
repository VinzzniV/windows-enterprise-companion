using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Wec.Core.Ccrx;
using Wec.Core.Results;

namespace Wec.Infrastructure.Ccrx;

/// <summary>
/// Reads Kyocera Command Center RX settings over HTTPS: establishes a session,
/// performs the plain form login (no hash/challenge in this generation), then
/// GETs the requested <c>*.model.htm</c> files and parses their properties.
/// Read-only — no printer setting is ever changed. The admin password never
/// reaches a log or an exception message.
/// </summary>
public sealed partial class CcrxHttpClient : ICcrxClient
{
    private const string LoginPath = "/startwlm/login.cgi";
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36";

    private readonly ILogger<CcrxHttpClient> _logger;
    private readonly Func<HttpMessageHandler> _handlerFactory;
    private readonly TimeSpan _timeout;

    public CcrxHttpClient(ILogger<CcrxHttpClient> logger)
        : this(logger, CreateTrustingHandler, TimeSpan.FromSeconds(15))
    {
    }

    // Tests inject a fake handler and a short timeout.
    internal CcrxHttpClient(ILogger<CcrxHttpClient> logger, Func<HttpMessageHandler> handlerFactory, TimeSpan timeout)
    {
        _logger = logger;
        _handlerFactory = handlerFactory;
        _timeout = timeout;
    }

    public async Task<Result<CcrxReadResult>> ReadModelsAsync(
        string host,
        CcrxCredentials credentials,
        IReadOnlyList<string> modelPaths,
        CancellationToken cancellationToken)
    {
        var baseUri = new Uri($"https://{host}");
        using HttpMessageHandler handler = _handlerFactory();
        using var http = new HttpClient(handler, disposeHandler: false) { Timeout = _timeout };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,*/*");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "de,en;q=0.9");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", $"{baseUri}startwlm/Start_Wlm.htm");

        try
        {
            // 1) Establish the session cookie the embedded server requires.
            using (var rootResponse = await http.GetAsync(baseUri, cancellationToken))
            {
                rootResponse.EnsureSuccessStatusCode();
            }

            // 2) Plain form login (fields observed on the device).
            using var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["func"] = "authLogin",
                ["arg01_UserName"] = credentials.UserName,
                ["arg02_Password"] = credentials.Password,
                ["arg03_LoginType"] = "_mode_off",
                ["arg04_LoginFrom"] = "_wlm_login",
                ["arg05_AccountId"] = string.Empty,
                ["arg06_DomainName"] = string.Empty,
                ["okhtmfile"] = "/startwlm/Start_Wlm.htm",
                ["failhtmfile"] = "/startwlm/Start_Wlm.htm",
            });
            using (var loginResponse = await http.PostAsync(new Uri(baseUri, LoginPath), loginContent, cancellationToken))
            {
                loginResponse.EnsureSuccessStatusCode();
            }

            // 3) Read each requested model.
            var models = new List<CcrxModel>(modelPaths.Count);
            foreach (string path in modelPaths)
            {
                using var modelResponse = await http.GetAsync(new Uri(baseUri, path), cancellationToken);
                modelResponse.EnsureSuccessStatusCode();
                string body = await modelResponse.Content.ReadAsStringAsync(cancellationToken);
                models.Add(new CcrxModel(path, CcrxModelParser.Parse(body)));
            }

            // CCRX serves a small login-gate stub (no properties) when the login
            // did not take — surface that as an authentication failure, not "all clear".
            if (models.Count > 0 && models.All(model => model.Properties.Count == 0))
            {
                LogAuthFailed(host);
                return Result.Failure<CcrxReadResult>(new Error(
                    ErrorCode.AuthenticationFailed,
                    "The Command Center RX login was not accepted — check the admin password."));
            }

            return Result.Success(new CcrxReadResult(models));
        }
        catch (HttpRequestException exception)
        {
            LogRequestFailed(host, exception.Message);
            return Result.Failure<CcrxReadResult>(new Error(
                ErrorCode.RemoteCommandFailed,
                $"The Command Center RX on '{host}' could not be reached over HTTPS.")
            {
                Details = exception.Message,
            });
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<CcrxReadResult>(new Error(
                ErrorCode.ConnectionTimeout, $"The Command Center RX on '{host}' did not answer in time."));
        }
    }

    private static HttpMessageHandler CreateTrustingHandler() =>
        // Printers use self-signed certs; a per-call CookieContainer keeps each
        // device session isolated.
        new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = true,
        };

    [LoggerMessage(Level = LogLevel.Warning, Message = "CCRX login not accepted on {Host}")]
    private partial void LogAuthFailed(string host);

    [LoggerMessage(Level = LogLevel.Warning, Message = "CCRX request to {Host} failed: {Reason}")]
    private partial void LogRequestFailed(string host, string reason);
}
