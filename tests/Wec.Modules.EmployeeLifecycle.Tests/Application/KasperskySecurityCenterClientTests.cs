using System.Net;
using System.Security.Authentication;
using System.Text;
using Wec.Core.Results;
using Wec.Modules.EmployeeLifecycle.Application;

namespace Wec.Modules.EmployeeLifecycle.Tests.Application;

public sealed class KasperskySecurityCenterClientTests
{
    [Fact]
    public async Task Load_AuthenticatesAndMapsHostInventory()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, "{}"),
            Json(HttpStatusCode.OK, """
                {"PxgRetVal":1,"strAccessor":"hosts-1"}
                """),
            Json(HttpStatusCode.OK, """
                {
                  "PxgRetVal": 1,
                  "pChunk": {
                    "KLCSP_ITERATOR_ARRAY": [
                      {
                        "type": "params",
                        "value": {
                          "KLHST_WKS_WINHOSTNAME": "PC001",
                          "KLHST_WKS_FQDN": "pc001.example.test",
                          "KLHST_WKS_DNSNAME": "pc001",
                          "KLHST_WKS_DN": "native-record-name",
                          "KLHST_WKS_LAST_VISIBLE": {"type":"datetime","value":"2026-08-16T10:30:00Z"},
                          "KLHST_WKS_NAG_VERSION": "16.0.0.254",
                          "KLHST_WKS_RTP_AV_VERSION": "21.25.7.504",
                          "grp_full_name": "Managed devices/Workstations"
                        }
                      }
                    ]
                  }
                }
                """),
            Json(HttpStatusCode.OK, "{}"));
        var client = new KasperskySecurityCenterClient(handler);

        Result<KasperskyInventory> result = await client.LoadAsync(Connection(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        KasperskyComputer computer = Assert.Single(result.Value.Computers);
        Assert.Equal("PC001", computer.ComputerName);
        Assert.Equal("pc001.example.test", computer.Fqdn);
        Assert.Equal("pc001", computer.DnsName);
        Assert.Equal("native-record-name", computer.RecordName);
        Assert.Equal(new DateTimeOffset(2026, 8, 16, 10, 30, 0, TimeSpan.Zero), computer.LastSeen);
        Assert.Equal("16.0.0.254", computer.AgentVersion);
        Assert.Equal("21.25.7.504", computer.KesVersion);
        Assert.Equal("Managed devices/Workstations", computer.AdministrationGroup);
        Assert.False(result.Value.Truncated);

        Assert.Equal(
            ["login", "HostGroup.FindHosts", "ChunkAccessor.GetItemsChunk", "ChunkAccessor.Release"],
            handler.RequestPaths);
        Assert.Contains("KSCBasic", handler.AuthorizationHeaders[0], StringComparison.Ordinal);
        Assert.DoesNotContain("secret", handler.AuthorizationHeaders[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_ReportsAuthenticationFailure()
    {
        var handler = new QueueHandler(Unauthorized(), Unauthorized());
        var client = new KasperskySecurityCenterClient(handler);

        Result<KasperskyInventory> result = await client.LoadAsync(Connection(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.AuthenticationFailed, result.Error!.Code);
        Assert.Equal(["login", "login"], handler.RequestPaths);
    }

    [Fact]
    public async Task Load_RetriesOneTransientAuthenticationFailure()
    {
        var handler = new QueueHandler(
            Unauthorized(),
            Json(HttpStatusCode.OK, "{}"),
            Json(HttpStatusCode.OK, """{"PxgRetVal":0,"strAccessor":"hosts-1"}"""),
            Json(HttpStatusCode.OK, "{}"));
        var client = new KasperskySecurityCenterClient(handler);

        Result<KasperskyInventory> result = await client.LoadAsync(Connection(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Computers);
        Assert.Equal(["login", "login", "HostGroup.FindHosts", "ChunkAccessor.Release"], handler.RequestPaths);
    }

    [Fact]
    public async Task Load_ExcludesDevicesBelowConfiguredAdministrationGroup()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, "{}"),
            Json(HttpStatusCode.OK, """{"PxgRetVal":2,"strAccessor":"hosts-1"}"""),
            Json(HttpStatusCode.OK, """
                {"pChunk":{"KLCSP_ITERATOR_ARRAY":[
                  {"type":"params","value":{"KLHST_WKS_DN":"PRINTER01","grp_full_name":"Managed devices/Denkingen/Nicht für Kaspersky geeignete Geräte"}},
                  {"type":"params","value":{"KLHST_WKS_DN":"PC001","grp_full_name":"Managed devices/Denkingen/Clients"}}
                ]}}
                """),
            Json(HttpStatusCode.OK, "{}"));
        var client = new KasperskySecurityCenterClient(handler);

        Result<KasperskyInventory> result = await client.LoadAsync(Connection(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("PC001", Assert.Single(result.Value.Computers).ComputerName);
    }

    [Fact]
    public async Task Load_StopsAtConfiguredLimitAndMarksResultTruncated()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, "{}"),
            Json(HttpStatusCode.OK, """{"PxgRetVal":2,"strAccessor":"hosts-1"}"""),
            Json(HttpStatusCode.OK, """
                {"pChunk":{"KLCSP_ITERATOR_ARRAY":[
                  {"type":"params","value":{"KLHST_WKS_DN":"PC001"}}
                ]}}
                """),
            Json(HttpStatusCode.OK, "{}"));
        var client = new KasperskySecurityCenterClient(handler);

        Result<KasperskyInventory> result = await client.LoadAsync(
            Connection() with { InventoryLimit = 1 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Computers);
        Assert.True(result.Value.Truncated);
    }

    [Fact]
    public async Task Load_ExplainsHowToResolveCertificateValidationFailure()
    {
        var client = new KasperskySecurityCenterClient(new ThrowingHandler(
            new HttpRequestException(
                "The SSL connection could not be established.",
                new AuthenticationException("The remote certificate is invalid."))));

        Result<KasperskyInventory> result = await client.LoadAsync(Connection(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("TLS certificate", result.Error!.Message, StringComparison.Ordinal);
        Assert.Contains("KSC certificate thumbprint", result.Error.Details, StringComparison.Ordinal);
        Assert.Contains("remote certificate is invalid", result.Error.Details, StringComparison.Ordinal);
    }

    private static KasperskyConnection Connection() => new(
        "ksc.example.test",
        13_299,
        "reader",
        "EXAMPLE",
        "secret",
        TrustedCertificateThumbprint: null,
        Timeout: TimeSpan.FromSeconds(10),
        InventoryLimit: 100,
        ExcludedAdministrationGroups: ["Nicht für Kaspersky geeignete Geräte"]);

    private static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Unauthorized() => new(HttpStatusCode.Unauthorized)
    {
        Content = new StringContent("invalid credentials", Encoding.UTF8, "text/plain"),
    };

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<string> RequestPaths { get; } = [];

        public List<string> AuthorizationHeaders { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestPaths.Add(request.RequestUri!.Segments[^1]);
            if (request.Headers.TryGetValues("Authorization", out IEnumerable<string>? values))
            {
                AuthorizationHeaders.Add(values.Single());
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromException<HttpResponseMessage>(exception);
    }
}
