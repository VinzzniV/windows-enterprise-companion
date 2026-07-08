using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Wec.Core.Ccrx;
using Wec.Core.Results;
using Wec.Infrastructure.Ccrx;

namespace Wec.Infrastructure.IntegrationTests.Ccrx;

/// <summary>
/// Drives the CCRX login + model-read flow against a fake HTTP handler — proving
/// the session/login/parse chain and the login-gate detection without a device.
/// </summary>
public sealed class CcrxHttpClientTests
{
    private const string EmailPath = "/js/jssrc/model/funcset/email/FuncSet_Email_EmailSet.model.htm";

    private static CcrxHttpClient CreateClient(FakeHandler handler) =>
        new(NullLogger<CcrxHttpClient>.Instance, () => handler, TimeSpan.FromSeconds(5));

    [Fact]
    public async Task ReadModelsAsync_LoggedIn_ReturnsParsedProperties()
    {
        var handler = new FakeHandler((method, path) => path switch
        {
            "/" => Ok("<frameset>"),
            "/startwlm/login.cgi" => Ok("<html>ok</html>"),
            EmailPath => Ok("_pp.smtpMode = '_mode_on'; _pp.smtpServerName = 'pk-srvmail.kauth.local';"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        Result<CcrxReadResult> result = await CreateClient(handler).ReadModelsAsync(
            "printer.local", CcrxCredentials.Default, [EmailPath], CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        CcrxModel model = Assert.Single(result.Value.Models);
        Assert.Equal("_mode_on", model.Get("smtpMode"));
        Assert.Equal("pk-srvmail.kauth.local", model.Get("smtpServerName"));
        Assert.True(handler.PostedTo("/startwlm/login.cgi"));
    }

    [Fact]
    public async Task ReadModelsAsync_LoginGateStub_ReturnsAuthenticationFailed()
    {
        // CCRX serves a stub with no _pp.* when the login was not accepted.
        var handler = new FakeHandler((method, path) => path switch
        {
            "/" => Ok("<frameset>"),
            "/startwlm/login.cgi" => Ok("<html>fail</html>"),
            EmailPath => Ok("<html><body>login required</body></html>"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        Result<CcrxReadResult> result = await CreateClient(handler).ReadModelsAsync(
            "printer.local", CcrxCredentials.Default, [EmailPath], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.AuthenticationFailed, result.Error!.Code);
    }

    [Fact]
    public async Task ReadModelsAsync_ConnectionRefused_ReturnsRemoteCommandFailed()
    {
        var handler = new FakeHandler((_, _) => throw new HttpRequestException("connection refused"));

        Result<CcrxReadResult> result = await CreateClient(handler).ReadModelsAsync(
            "printer.local", CcrxCredentials.Default, [EmailPath], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.RemoteCommandFailed, result.Error!.Code);
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpMethod, string, HttpResponseMessage> _respond;
        private readonly List<(HttpMethod Method, string Path)> _requests = [];

        public FakeHandler(Func<HttpMethod, string, HttpResponseMessage> respond) => _respond = respond;

        public bool PostedTo(string path) =>
            _requests.Any(request => request.Method == HttpMethod.Post && request.Path == path);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            return Task.FromResult(_respond(request.Method, request.RequestUri!.AbsolutePath));
        }
    }
}
