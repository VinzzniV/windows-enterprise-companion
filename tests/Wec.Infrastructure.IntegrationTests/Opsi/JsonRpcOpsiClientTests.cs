using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Wec.Core.Opsi;
using Wec.Core.Results;
using Wec.Infrastructure.Opsi;

namespace Wec.Infrastructure.IntegrationTests.Opsi;

public sealed class JsonRpcOpsiClientTests
{
    private static readonly OpsiConnection Connection = new(
        new Uri("https://opsi.example.test:4447"),
        "admin",
        "secret",
        TrustServerCertificate: false,
        RequestTimeout: TimeSpan.FromSeconds(5));

    [Fact]
    public async Task TestConnection_ReadsOpsiVersion()
    {
        using var client = CreateClient("""{"id":1,"error":null,"result":{"opsiVersion":"4.3.1.2"}}""");

        Result<OpsiServerInfo> result = await client.TestConnectionAsync(Connection, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("4.3.1.2", result.Value.OpsiVersion);
    }

    [Fact]
    public async Task Unauthorized_MapsToAuthenticationFailed()
    {
        using var client = CreateClient(string.Empty, HttpStatusCode.Unauthorized);

        Result<OpsiServerInfo> result = await client.TestConnectionAsync(Connection, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.AuthenticationFailed, result.Error!.Code);
    }

    [Fact]
    public async Task Forbidden_MapsToAccessDeniedWithAdminGroupHint()
    {
        using var client = CreateClient(string.Empty, HttpStatusCode.Forbidden);

        Result<OpsiServerInfo> result = await client.TestConnectionAsync(Connection, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.AccessDenied, result.Error!.Code);
        Assert.Contains("opsiadmin", result.Error.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonRpcError_MapsToRemoteCommandFailedWithMessage()
    {
        using var client = CreateClient(
            """{"id":1,"error":{"message":"Backend module disabled","class":"BackendError"},"result":null}""");

        Result<IReadOnlyList<OpsiDepot>> result = await client.GetDepotsAsync(Connection, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.RemoteCommandFailed, result.Error!.Code);
        Assert.Contains("Backend module disabled", result.Error.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonJsonAnswer_MapsToServiceUnavailable()
    {
        using var client = CreateClient("<html>not an rpc endpoint</html>");

        Result<OpsiServerInfo> result = await client.TestConnectionAsync(Connection, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.ServiceUnavailable, result.Error!.Code);
    }

    [Fact]
    public async Task TlsFailure_MapsToServiceUnavailableWithTrustHint()
    {
        using var client = CreateThrowingClient(new HttpRequestException(
            "SSL connection could not be established",
            new System.Security.Authentication.AuthenticationException("untrusted root")));

        Result<OpsiServerInfo> result = await client.TestConnectionAsync(Connection, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.ServiceUnavailable, result.Error!.Code);
        Assert.Contains("Trust server certificate", result.Error.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownHost_MapsToDnsResolutionFailed()
    {
        using var client = CreateThrowingClient(new HttpRequestException(
            "No such host is known",
            new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostNotFound)));

        Result<OpsiServerInfo> result = await client.TestConnectionAsync(Connection, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.DnsResolutionFailed, result.Error!.Code);
    }

    [Fact]
    public async Task GetClients_MergesDepotAssignmentFromConfigStates()
    {
        using var client = CreateSequenceClient(
            """
            {"id":1,"error":null,"result":[
                {"id":"pc1.kauth.local","type":"OpsiClient","description":"Office PC","lastSeen":"2026-07-01 08:00:00"},
                {"id":"pc2.kauth.local","type":"OpsiClient","description":null,"lastSeen":null}
            ]}
            """,
            """
            {"id":1,"error":null,"result":[
                {"configId":"clientconfig.depot.id","objectId":"pc1.kauth.local","values":["depot-denkingen.kauth.local"]}
            ]}
            """);

        Result<IReadOnlyList<OpsiClientHost>> result = await client.GetClientsAsync(Connection, CancellationToken.None);

        Assert.True(result.IsSuccess);
        OpsiClientHost first = Assert.Single(result.Value, host => host.Id == "pc1.kauth.local");
        Assert.Equal("depot-denkingen.kauth.local", first.DepotId);
        Assert.NotNull(first.LastSeen);
        OpsiClientHost second = Assert.Single(result.Value, host => host.Id == "pc2.kauth.local");
        Assert.Null(second.DepotId);
    }

    [Fact]
    public async Task GetDepots_MarksConfigserver()
    {
        using var client = CreateClient(
            """
            {"id":1,"error":null,"result":[
                {"id":"opsi.kauth.local","type":"OpsiConfigserver","description":"Main server"},
                {"id":"depot-denkingen.kauth.local","type":"OpsiDepotserver","description":"Denkingen"}
            ]}
            """);

        Result<IReadOnlyList<OpsiDepot>> result = await client.GetDepotsAsync(Connection, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(result.Value, depot => depot.Id == "opsi.kauth.local").IsConfigServer);
        Assert.False(Assert.Single(result.Value, depot => depot.Id == "depot-denkingen.kauth.local").IsConfigServer);
    }

    [Fact]
    public async Task RequestSetup_SendsProductOnClientObjectsAndBasicAuth()
    {
        var handler = new RecordingHandler("""{"id":1,"error":null,"result":null}""");
        using var client = new JsonRpcOpsiClient(NullLogger<JsonRpcOpsiClient>.Instance, handler);

        Result<int> result = await client.RequestSetupAsync(
            Connection, "firefox", ["pc1.kauth.local", "pc2.kauth.local"], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);
        using JsonDocument sent = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("productOnClient_updateObjects", sent.RootElement.GetProperty("method").GetString());
        JsonElement objects = sent.RootElement.GetProperty("params")[0];
        Assert.Equal(2, objects.GetArrayLength());
        Assert.Equal("setup", objects[0].GetProperty("actionRequest").GetString());
        Assert.Equal("firefox", objects[0].GetProperty("productId").GetString());
        Assert.Equal(
            "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:secret")),
            handler.LastAuthorizationHeader);
    }

    [Fact]
    public async Task RequestSetup_WithoutClients_IsRejectedWithoutAnyCall()
    {
        var handler = new RecordingHandler("""{"id":1,"error":null,"result":null}""");
        using var client = new JsonRpcOpsiClient(NullLogger<JsonRpcOpsiClient>.Instance, handler);

        Result<int> result = await client.RequestSetupAsync(Connection, "firefox", [], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, result.Error!.Code);
        Assert.Null(handler.LastRequestBody);
    }

    private static JsonRpcOpsiClient CreateClient(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(NullLogger<JsonRpcOpsiClient>.Instance, new RecordingHandler(responseBody, statusCode));

    private static JsonRpcOpsiClient CreateSequenceClient(params string[] responseBodies) =>
        new(NullLogger<JsonRpcOpsiClient>.Instance, new SequenceHandler(responseBodies));

    private static JsonRpcOpsiClient CreateThrowingClient(Exception exception) =>
        new(NullLogger<JsonRpcOpsiClient>.Instance, new ThrowingHandler(exception));

    private sealed class RecordingHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        public string? LastAuthorizationHeader { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            LastAuthorizationHeader = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(statusCode) { Content = new StringContent(responseBody) };
        }
    }

    private sealed class SequenceHandler(string[] responseBodies) : HttpMessageHandler
    {
        private int _callIndex;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBodies[_callIndex++]),
            });
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => throw exception;
    }
}
