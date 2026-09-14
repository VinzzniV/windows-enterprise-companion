using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Wec.Core.Results;

namespace Wec.Modules.EmployeeLifecycle.Application;

internal sealed record KasperskyConnection(
    string Server,
    int Port,
    string UserName,
    string? Domain,
    string Password,
    string? TrustedCertificateThumbprint,
    TimeSpan Timeout,
    int InventoryLimit,
    IReadOnlyList<string> ExcludedAdministrationGroups);

public sealed record KasperskyComputer(
    string ComputerName,
    DateTimeOffset? LastSeen,
    string? AgentVersion,
    string? KesVersion,
    string? AdministrationGroup,
    string? Fqdn = null,
    string? DnsName = null,
    string? RecordName = null);

public sealed record KasperskyInventory(
    IReadOnlyList<KasperskyComputer> Computers,
    bool Truncated);

/// <summary>
/// Minimal read-only KSC OpenAPI client. It only authenticates, searches hosts,
/// reads result chunks and releases the temporary server-side result set.
/// </summary>
internal interface IKasperskyInventoryReader
{
    Task<Result<KasperskyInventory>> LoadAsync(
        KasperskyConnection connection,
        CancellationToken cancellationToken);
}

internal sealed class KasperskySecurityCenterClient : IKasperskyInventoryReader
{
    private static readonly TimeSpan LoginRetryDelay = TimeSpan.FromMilliseconds(350);

    private static readonly string[] HostFields =
    [
        "KLHST_WKS_DN",
        "KLHST_WKS_WINHOSTNAME",
        "KLHST_WKS_DNSNAME",
        "KLHST_WKS_FQDN",
        "KLHST_WKS_LAST_VISIBLE",
        "KLHST_WKS_NAG_VERSION",
        "KLHST_WKS_RTP_AV_VERSION",
        "name",
        "grp_full_name",
    ];

    private const int ChunkSize = 500;
    private readonly HttpMessageHandler? _testHandler;

    public KasperskySecurityCenterClient()
    {
    }

    internal KasperskySecurityCenterClient(HttpMessageHandler testHandler)
    {
        _testHandler = testHandler;
    }

    public async Task<Result<KasperskyInventory>> LoadAsync(
        KasperskyConnection connection,
        CancellationToken cancellationToken)
    {
        Result<Uri> endpoint = BuildEndpoint(connection.Server, connection.Port);
        if (endpoint.IsFailure)
        {
            return Result.Failure<KasperskyInventory>(endpoint.Error!);
        }

        using HttpMessageHandler handler = _testHandler ?? CreateHandler(connection.TrustedCertificateThumbprint);
        using var client = new HttpClient(handler, disposeHandler: _testHandler is null)
        {
            BaseAddress = endpoint.Value,
            Timeout = connection.Timeout,
        };

        string authorization = BuildAuthorization(connection);
        Result<JsonElement> login = await PostAsync(
            client,
            "login",
            new { },
            authorization,
            cancellationToken);
        if (login.IsFailure && login.Error!.Code == ErrorCode.AuthenticationFailed)
        {
            // KSC occasionally rejects the first login while its OpenAPI endpoint
            // is becoming ready. Retry once; persistent bad credentials still fail.
            await Task.Delay(LoginRetryDelay, cancellationToken);
            login = await PostAsync(
                client,
                "login",
                new { },
                authorization,
                cancellationToken);
        }

        if (login.IsFailure)
        {
            return Result.Failure<KasperskyInventory>(login.Error!);
        }

        Result<JsonElement> search = await PostAsync(
            client,
            "HostGroup.FindHosts",
            new
            {
                wstrFilter = string.Empty,
                vecFieldsToReturn = HostFields,
                vecFieldsToOrder = Array.Empty<string>(),
                pParams = new Dictionary<string, object>
                {
                    ["KLGRP_FIND_FROM_CUR_VS_ONLY"] = true,
                },
                lMaxLifeTime = 300,
            },
            authorization: null,
            cancellationToken);
        if (search.IsFailure)
        {
            return Result.Failure<KasperskyInventory>(search.Error!);
        }

        string? accessor = GetString(search.Value, "strAccessor");
        int count = GetInt(search.Value, "PxgRetVal") ?? 0;
        if (string.IsNullOrWhiteSpace(accessor))
        {
            return Result.Failure<KasperskyInventory>(new Error(
                ErrorCode.ServiceUnavailable,
                "Kaspersky Security Center returned no host result accessor."));
        }

        int limit = Math.Max(1, connection.InventoryLimit);
        int take = Math.Min(count, limit);
        var computers = new List<KasperskyComputer>(take);
        try
        {
            for (int start = 0; start < take; start += ChunkSize)
            {
                int chunkCount = Math.Min(ChunkSize, take - start);
                Result<JsonElement> chunk = await PostAsync(
                    client,
                    "ChunkAccessor.GetItemsChunk",
                    new { strAccessor = accessor, nStart = start, nCount = chunkCount },
                    authorization: null,
                    cancellationToken);
                if (chunk.IsFailure)
                {
                    return Result.Failure<KasperskyInventory>(chunk.Error!);
                }

                computers.AddRange(ParseChunk(chunk.Value).Where(computer =>
                    !IsExcludedAdministrationGroup(
                        computer.AdministrationGroup,
                        connection.ExcludedAdministrationGroups)));
            }
        }
        finally
        {
            await PostAsync(
                client,
                "ChunkAccessor.Release",
                new { strAccessor = accessor },
                authorization: null,
                CancellationToken.None);
        }

        return Result.Success(new KasperskyInventory(computers, Truncated: count > limit));
    }

    private static List<KasperskyComputer> ParseChunk(JsonElement response)
    {
        if (!response.TryGetProperty("pChunk", out JsonElement chunk)
            || !chunk.TryGetProperty("KLCSP_ITERATOR_ARRAY", out JsonElement items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<KasperskyComputer>();
        foreach (JsonElement item in items.EnumerateArray())
        {
            JsonElement values = item;
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("type", out JsonElement type)
                && type.GetString() == "params"
                && item.TryGetProperty("value", out JsonElement wrappedValues))
            {
                values = wrappedValues;
            }

            string? computerName = FirstNotEmpty(
                GetString(values, "KLHST_WKS_WINHOSTNAME"),
                GetString(values, "KLHST_WKS_DNSNAME"),
                GetString(values, "KLHST_WKS_FQDN"),
                GetString(values, "KLHST_WKS_DN"));
            if (computerName is null)
            {
                continue;
            }

            result.Add(new KasperskyComputer(
                computerName,
                GetDateTime(values, "KLHST_WKS_LAST_VISIBLE"),
                GetString(values, "KLHST_WKS_NAG_VERSION"),
                GetString(values, "KLHST_WKS_RTP_AV_VERSION"),
                FirstNotEmpty(
                    GetString(values, "grp_full_name"),
                    GetString(values, "name")),
                GetString(values, "KLHST_WKS_FQDN"),
                GetString(values, "KLHST_WKS_DNSNAME"),
                GetString(values, "KLHST_WKS_DN")));
        }

        return result;
    }

    internal static bool IsExcludedAdministrationGroup(
        string? administrationGroup,
        IReadOnlyList<string> excludedGroups)
    {
        if (string.IsNullOrWhiteSpace(administrationGroup) || excludedGroups.Count == 0)
        {
            return false;
        }

        string[] segments = administrationGroup.Split(
            ['/', '\\'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return excludedGroups
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Any(group => segments.Contains(group.Trim(), StringComparer.OrdinalIgnoreCase));
    }

    private static async Task<Result<JsonElement>> PostAsync(
        HttpClient client,
        string method,
        object payload,
        string? authorization,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, method)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"),
            Version = HttpVersion.Version11,
        };
        request.Headers.TryAddWithoutValidation("X-KSC-RequestId", Guid.NewGuid().ToString("N"));
        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                ErrorCode code = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => ErrorCode.AuthenticationFailed,
                    HttpStatusCode.Forbidden => ErrorCode.AccessDenied,
                    _ => ErrorCode.ServiceUnavailable,
                };
                return Result.Failure<JsonElement>(new Error(
                    code,
                    $"Kaspersky Security Center request '{method}' failed ({(int)response.StatusCode}).")
                {
                    Details = Limit(body, 1_000),
                });
            }

            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("PxgError", out JsonElement apiError))
            {
                return Result.Failure<JsonElement>(new Error(
                    ErrorCode.ServiceUnavailable,
                    $"Kaspersky Security Center rejected '{method}'.")
                {
                    Details = Limit(apiError.ToString(), 1_000),
                });
            }

            return Result.Success(root.Clone());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<JsonElement>(new Error(
                ErrorCode.ConnectionTimeout,
                $"Kaspersky Security Center request '{method}' timed out."));
        }
        catch (HttpRequestException exception)
        {
            return Result.Failure<JsonElement>(MapTransportException(exception));
        }
        catch (JsonException exception)
        {
            return Result.Failure<JsonElement>(new Error(
                ErrorCode.ServiceUnavailable,
                "Kaspersky Security Center returned invalid JSON.")
            {
                Details = exception.Message,
            });
        }
    }

    private static Error MapTransportException(HttpRequestException exception)
    {
        string details = ExceptionMessages(exception);
        bool tlsFailure = details.Contains("SSL", StringComparison.OrdinalIgnoreCase)
            || details.Contains("TLS", StringComparison.OrdinalIgnoreCase)
            || details.Contains("certificate", StringComparison.OrdinalIgnoreCase)
            || details.Contains("AuthenticationException", StringComparison.OrdinalIgnoreCase);

        return new Error(
            ErrorCode.ServiceUnavailable,
            tlsFailure
                ? "The TLS certificate of Kaspersky Security Center could not be validated."
                : "Kaspersky Security Center is unavailable.")
        {
            Details = tlsFailure
                ? "Enter the KSC server certificate's SHA-256 or SHA-1 thumbprint under Settings → "
                    + "IT Lifecycle → KSC certificate thumbprint, or install its issuing CA in the Windows "
                    + $"certificate store. Restart WEC after saving. Technical detail: {details}"
                : details,
        };
    }

    private static string ExceptionMessages(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(current.Message)
                && !messages.Contains(current.Message, StringComparer.Ordinal))
            {
                messages.Add(current.Message);
            }
        }

        return Limit(string.Join(" → ", messages), 1_500);
    }

    private static Result<Uri> BuildEndpoint(string server, int port)
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            return Result.Failure<Uri>(new Error(
                ErrorCode.InvalidRequest,
                "A Kaspersky Security Center server is required."));
        }

        string value = server.Trim();
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = $"https://{value}";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed)
            || parsed.Scheme != Uri.UriSchemeHttps)
        {
            return Result.Failure<Uri>(new Error(
                ErrorCode.InvalidRequest,
                "The Kaspersky server must be a valid HTTPS host name or URL."));
        }

        var builder = new UriBuilder(parsed)
        {
            Port = port,
            Path = "/api/v1.0/",
            Query = string.Empty,
            Fragment = string.Empty,
        };
        return Result.Success(builder.Uri);
    }

    private static HttpClientHandler CreateHandler(string? trustedThumbprint)
    {
        var handler = new HttpClientHandler();
        string normalizedThumbprint = NormalizeThumbprint(trustedThumbprint);
        if (normalizedThumbprint.Length > 0)
        {
            handler.ServerCertificateCustomValidationCallback =
                (_, certificate, _, errors) =>
                    errors == SslPolicyErrors.None
                    || CertificateMatches(certificate, normalizedThumbprint);
        }

        return handler;
    }

    private static bool CertificateMatches(X509Certificate2? certificate, string trustedThumbprint) =>
        certificate is not null
        && (string.Equals(
                NormalizeThumbprint(certificate.GetCertHashString()),
                trustedThumbprint,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                NormalizeThumbprint(certificate.GetCertHashString(
                    System.Security.Cryptography.HashAlgorithmName.SHA256)),
                trustedThumbprint,
                StringComparison.OrdinalIgnoreCase));

    private static string NormalizeThumbprint(string? value) =>
        string.Concat((value ?? string.Empty).Where(Uri.IsHexDigit)).ToUpperInvariant();

    private static string BuildAuthorization(KasperskyConnection connection)
    {
        static string Base64(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

        var parts = new List<string>
        {
            $"user=\"{Base64(connection.UserName)}\"",
            $"pass=\"{Base64(connection.Password)}\"",
        };
        if (!string.IsNullOrWhiteSpace(connection.Domain))
        {
            parts.Add($"domain=\"{Base64(connection.Domain)}\"");
        }

        parts.Add("internal=\"0\"");
        return $"KSCBasic {string.Join(", ", parts)}";
    }

    private static string? GetString(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("value", out JsonElement wrapped)
            && wrapped.ValueKind == JsonValueKind.String
                ? wrapped.GetString()
                : null;
    }

    private static int? GetInt(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(name, out JsonElement value)
        && value.TryGetInt32(out int parsed)
            ? parsed
            : null;

    private static DateTimeOffset? GetDateTime(JsonElement parent, string name)
    {
        string? value = GetString(parent, name);
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    private static string? FirstNotEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string Limit(string value, int length) =>
        value.Length <= length ? value : value[..length];
}
