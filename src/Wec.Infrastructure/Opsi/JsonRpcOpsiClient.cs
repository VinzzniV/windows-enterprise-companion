using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wec.Core.Opsi;
using Wec.Core.Results;

namespace Wec.Infrastructure.Opsi;

/// <summary>
/// opsi access over the opsiconfd JSON-RPC API (ADR 0008) — the same API
/// opsi-configed uses. Basic auth per request; the shared HttpClients carry
/// no credential state, so one instance serves any number of connections.
/// </summary>
public sealed partial class JsonRpcOpsiClient : IOpsiClient, IDisposable
{
    private const string LocalbootProductType = "LocalbootProduct";

    private readonly ILogger<JsonRpcOpsiClient> _logger;
    private readonly HttpClient _validatingClient;
    private readonly HttpClient _trustingClient;

    public JsonRpcOpsiClient(ILogger<JsonRpcOpsiClient> logger)
        : this(logger, handlerOverride: null)
    {
    }

    // Tests inject a fake handler; both clients then share it
    internal JsonRpcOpsiClient(ILogger<JsonRpcOpsiClient> logger, HttpMessageHandler? handlerOverride)
    {
        _logger = logger;
        _validatingClient = CreateClient(handlerOverride ?? new SocketsHttpHandler());
#pragma warning disable CA5359 // ADR 0008: this client is only used after the
        // admin explicitly ticks "Trust server certificate" for the session
        // (opsi's default CA is self-signed); the default client above keeps
        // full chain validation
        _trustingClient = CreateClient(handlerOverride ?? new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            },
        });
#pragma warning restore CA5359
    }

    public async Task<Result<OpsiServerInfo>> TestConnectionAsync(
        OpsiConnection connection, CancellationToken cancellationToken)
    {
        Result<JsonElement> result = await CallAsync(
            connection, "backend_info", [], cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Result.Success(new OpsiServerInfo(GetString(result.Value, "opsiVersion")))
            : Result.Failure<OpsiServerInfo>(result.Error!);
    }

    public Task<Result<IReadOnlyList<OpsiDepot>>> GetDepotsAsync(
        OpsiConnection connection, CancellationToken cancellationToken) =>
        QueryObjectsAsync(
            connection,
            "host_getObjects",
            new Dictionary<string, object?> { ["type"] = "OpsiDepotserver" },
            element => GetString(element, "id") is { } id
                ? new OpsiDepot(id, GetString(element, "description"), GetString(element, "type") == "OpsiConfigserver")
                : null,
            cancellationToken);

    public async Task<Result<IReadOnlyList<OpsiClientHost>>> GetClientsAsync(
        OpsiConnection connection, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<OpsiClientHost>> hosts = await QueryObjectsAsync(
            connection,
            "host_getObjects",
            new Dictionary<string, object?> { ["type"] = "OpsiClient" },
            element => GetString(element, "id") is { } id
                ? new OpsiClientHost(
                    id,
                    GetString(element, "description"),
                    DepotId: null,
                    ParseOpsiTimestamp(GetString(element, "lastSeen")))
                : null,
            cancellationToken).ConfigureAwait(false);
        if (hosts.IsFailure)
        {
            return hosts;
        }

        // Depot assignment lives in a config state, not on the host object;
        // clients without one use the default depot (DepotId stays null)
        Result<JsonElement> configStates = await CallAsync(
            connection,
            "configState_getObjects",
            [Array.Empty<string>(), new Dictionary<string, object?> { ["configId"] = "clientconfig.depot.id" }],
            cancellationToken).ConfigureAwait(false);
        if (configStates.IsFailure)
        {
            return Result.Failure<IReadOnlyList<OpsiClientHost>>(configStates.Error!);
        }

        var depotByClient = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (configStates.Value.ValueKind is JsonValueKind.Array)
        {
            foreach (JsonElement state in configStates.Value.EnumerateArray())
            {
                if (GetString(state, "objectId") is { } clientId
                    && state.TryGetProperty("values", out JsonElement values)
                    && values.ValueKind is JsonValueKind.Array
                    && values.GetArrayLength() > 0
                    && values[0].ValueKind is JsonValueKind.String)
                {
                    depotByClient[clientId] = values[0].GetString()!;
                }
            }
        }

        return Result.Success<IReadOnlyList<OpsiClientHost>>(
        [
            .. hosts.Value.Select(host => depotByClient.TryGetValue(host.Id, out string? depotId)
                ? host with { DepotId = depotId }
                : host),
        ]);
    }

    public Task<Result<IReadOnlyList<OpsiProduct>>> GetProductsAsync(
        OpsiConnection connection, CancellationToken cancellationToken) =>
        QueryObjectsAsync(
            connection,
            "product_getObjects",
            new Dictionary<string, object?> { ["type"] = LocalbootProductType },
            element => GetString(element, "id") is { } id
                ? new OpsiProduct(
                    id,
                    GetString(element, "name"),
                    GetString(element, "productVersion") ?? string.Empty,
                    GetString(element, "packageVersion") ?? string.Empty,
                    GetString(element, "description"))
                : null,
            cancellationToken);

    public Task<Result<IReadOnlyList<OpsiProductOnDepot>>> GetProductsOnDepotsAsync(
        OpsiConnection connection, CancellationToken cancellationToken) =>
        QueryObjectsAsync(
            connection,
            "productOnDepot_getObjects",
            new Dictionary<string, object?> { ["productType"] = LocalbootProductType },
            element => GetString(element, "productId") is { } productId && GetString(element, "depotId") is { } depotId
                ? new OpsiProductOnDepot(
                    productId,
                    depotId,
                    GetString(element, "productVersion") ?? string.Empty,
                    GetString(element, "packageVersion") ?? string.Empty)
                : null,
            cancellationToken);

    public Task<Result<IReadOnlyList<OpsiProductOnClient>>> GetProductStatesAsync(
        OpsiConnection connection, CancellationToken cancellationToken) =>
        GetProductStatesCoreAsync(connection, productId: null, cancellationToken);

    public Task<Result<IReadOnlyList<OpsiProductOnClient>>> GetProductStatesAsync(
        OpsiConnection connection,
        string productId,
        CancellationToken cancellationToken) =>
        GetProductStatesCoreAsync(connection, string.IsNullOrWhiteSpace(productId) ? null : productId.Trim(), cancellationToken);

    private Task<Result<IReadOnlyList<OpsiProductOnClient>>> GetProductStatesCoreAsync(
        OpsiConnection connection,
        string? productId,
        CancellationToken cancellationToken) =>
        QueryObjectsAsync(
            connection,
            "productOnClient_getObjects",
            productId is null
                ? new Dictionary<string, object?> { ["productType"] = LocalbootProductType }
                : new Dictionary<string, object?>
                {
                    ["productType"] = LocalbootProductType,
                    ["productId"] = productId,
                },
            element => GetString(element, "productId") is { } productId && GetString(element, "clientId") is { } clientId
                ? new OpsiProductOnClient(
                    productId,
                    clientId,
                    GetString(element, "installationStatus"),
                    GetString(element, "actionRequest"),
                    GetString(element, "actionResult"),
                    GetString(element, "productVersion"),
                    GetString(element, "packageVersion"),
                    ParseOpsiTimestamp(GetString(element, "modificationTime")))
                : null,
            cancellationToken);

    public void Dispose()
    {
        _validatingClient.Dispose();
        _trustingClient.Dispose();
    }

    private static HttpClient CreateClient(HttpMessageHandler handler) =>
        // Per-call timeouts come from OpsiConnection.RequestTimeout
        new(handler) { Timeout = Timeout.InfiniteTimeSpan };

    private async Task<Result<IReadOnlyList<T>>> QueryObjectsAsync<T>(
        OpsiConnection connection,
        string method,
        Dictionary<string, object?> filter,
        Func<JsonElement, T?> map,
        CancellationToken cancellationToken)
        where T : class
    {
        Result<JsonElement> result = await CallAsync(
            connection, method, [Array.Empty<string>(), filter], cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Result.Failure<IReadOnlyList<T>>(result.Error!);
        }

        if (result.Value.ValueKind is not JsonValueKind.Array)
        {
            return Result.Failure<IReadOnlyList<T>>(NotJsonRpc(connection));
        }

        var items = new List<T>();
        foreach (JsonElement element in result.Value.EnumerateArray())
        {
            if (map(element) is { } item)
            {
                items.Add(item);
            }
        }

        return Result.Success<IReadOnlyList<T>>(items);
    }

    private async Task<Result<JsonElement>> CallAsync(
        OpsiConnection connection,
        string method,
        object?[] parameters,
        CancellationToken cancellationToken)
    {
        HttpClient client = connection.TrustServerCertificate ? _trustingClient : _validatingClient;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(connection.RequestTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(connection.ServiceUrl, "rpc"));
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{connection.UserName}:{connection.Password}")));
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { id = 1, method, @params = parameters }),
                Encoding.UTF8,
                "application/json");

            using HttpResponseMessage response =
                await client.SendAsync(request, timeoutSource.Token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized)
            {
                return Result.Failure<JsonElement>(new Error(
                    ErrorCode.AuthenticationFailed,
                    $"opsi rejected the credentials for user '{connection.UserName}'."));
            }

            if (response.StatusCode is HttpStatusCode.Forbidden)
            {
                return Result.Failure<JsonElement>(new Error(
                    ErrorCode.AccessDenied,
                    $"opsi denied '{connection.UserName}' access to '{method}'.")
                {
                    Details = "The user must be in the opsi admin group (default: opsiadmin) on the server.",
                });
            }

            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<JsonElement>(new Error(
                    ErrorCode.ServiceUnavailable,
                    $"The opsi service answered HTTP {(int)response.StatusCode} for '{method}'."));
            }

            await using Stream stream =
                await response.Content.ReadAsStreamAsync(timeoutSource.Token).ConfigureAwait(false);
            using JsonDocument document =
                await JsonDocument.ParseAsync(stream, cancellationToken: timeoutSource.Token).ConfigureAwait(false);

            if (document.RootElement.ValueKind is JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out JsonElement error)
                && error.ValueKind is not JsonValueKind.Null)
            {
                string message = error.ValueKind is JsonValueKind.Object
                    ? GetString(error, "message") ?? error.ToString()
                    : error.ToString();
                LogRpcError(method, message);
                return Result.Failure<JsonElement>(new Error(
                    ErrorCode.RemoteCommandFailed, $"opsi reported an error for '{method}'.")
                {
                    Details = message,
                });
            }

            if (document.RootElement.ValueKind is not JsonValueKind.Object
                || !document.RootElement.TryGetProperty("result", out JsonElement rpcResult))
            {
                return Result.Failure<JsonElement>(NotJsonRpc(connection));
            }

            return Result.Success(rpcResult.Clone());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<JsonElement>(new Error(
                ErrorCode.ConnectionTimeout,
                $"The opsi service at '{connection.ServiceUrl.Host}' did not answer within "
                + $"{connection.RequestTimeout.TotalSeconds:0} seconds."));
        }
        catch (HttpRequestException exception)
        {
            LogTransportError(method, exception.Message);
            return Result.Failure<JsonElement>(MapTransportException(exception, connection));
        }
        catch (JsonException)
        {
            return Result.Failure<JsonElement>(NotJsonRpc(connection));
        }
    }

    private static Error MapTransportException(HttpRequestException exception, OpsiConnection connection)
    {
        if (exception.InnerException is AuthenticationException)
        {
            return new Error(
                ErrorCode.ServiceUnavailable,
                $"The TLS certificate of '{connection.ServiceUrl.Host}' is not trusted.")
            {
                Details = "opsi uses a self-signed CA by default. Enable 'Trust server certificate' "
                    + "for this session or install the opsi CA certificate on this machine. "
                    + exception.Message,
            };
        }

        for (Exception? inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is SocketException socketException)
            {
                if (socketException.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData)
                {
                    return new Error(
                        ErrorCode.DnsResolutionFailed,
                        $"The opsi server '{connection.ServiceUrl.Host}' could not be resolved in DNS.");
                }

                return new Error(
                    ErrorCode.ServiceUnavailable,
                    $"The opsi service at '{connection.ServiceUrl.Host}:{connection.ServiceUrl.Port}' "
                    + "is not reachable.")
                {
                    Details = "opsiconfd not running or the port is blocked by a firewall. "
                        + socketException.Message,
                };
            }
        }

        return new Error(
            ErrorCode.ServiceUnavailable,
            $"The opsi service at '{connection.ServiceUrl.Host}' could not be reached.")
        {
            Details = exception.Message,
        };
    }

    private static Error NotJsonRpc(OpsiConnection connection) => new(
        ErrorCode.ServiceUnavailable,
        $"'{connection.ServiceUrl}' did not answer with JSON-RPC — is this an opsiconfd service URL "
        + "(usually https://<server>:4447)?");

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind is JsonValueKind.Object
        && element.TryGetProperty(propertyName, out JsonElement value)
        && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    // opsi timestamps ("2026-07-03 10:22:33") are server-local; display-only,
    // so the offset slack does not matter
    private static DateTimeOffset? ParseOpsiTimestamp(string? value) =>
        DateTimeOffset.TryParseExact(
            value,
            "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out DateTimeOffset parsed)
            ? parsed
            : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "opsi JSON-RPC '{Method}' failed: {RpcError}")]
    private partial void LogRpcError(string method, string rpcError);

    [LoggerMessage(Level = LogLevel.Warning, Message = "opsi transport failure during '{Method}': {Reason}")]
    private partial void LogTransportError(string method, string reason);
}
