using System.Text.RegularExpressions;
using Wec.Core.Results;
using Wec.Core.SoftwareUpdates;

namespace Wec.Infrastructure.SoftwareUpdates;

public sealed class HttpVendorVersionClient : IVendorVersionClient, IDisposable
{
    private const int MaximumResponseBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);
    private readonly HttpClient _httpClient;

    public HttpVendorVersionClient()
        : this(new HttpClientHandler())
    {
    }

    internal HttpVendorVersionClient(HttpMessageHandler handler)
    {
        _httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WindowsEnterpriseCompanion/1.0");
    }

    public async Task<Result<string>> GetLatestVersionAsync(
        VendorVersionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SourceUrl.Scheme != Uri.UriSchemeHttps)
        {
            return Result.Failure<string>(new Error(
                ErrorCode.InvalidRequest,
                "Vendor version sources must use HTTPS."));
        }

        if (string.IsNullOrWhiteSpace(request.VersionPattern))
        {
            return Result.Failure<string>(new Error(
                ErrorCode.InvalidRequest,
                "A vendor version source needs a regular expression with a capture group."));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(
                request.SourceUrl,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<string>(new Error(
                    ErrorCode.ServiceUnavailable,
                    $"The vendor source answered HTTP {(int)response.StatusCode}."));
            }

            if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            {
                return Result.Failure<string>(new Error(
                    ErrorCode.InvalidRequest,
                    "The vendor response exceeds the 2 MB safety limit."));
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            char[] buffer = new char[MaximumResponseBytes + 1];
            int length = await reader.ReadBlockAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false);
            if (length > MaximumResponseBytes)
            {
                return Result.Failure<string>(new Error(
                    ErrorCode.InvalidRequest,
                    "The vendor response exceeds the 2 MB safety limit."));
            }

            var expression = new Regex(
                request.VersionPattern,
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                RegexTimeout);
            Match match = expression.Match(new string(buffer, 0, length));
            string? version = match.Success && match.Groups.Count > 1
                ? match.Groups[1].Value.Trim()
                : null;
            return string.IsNullOrWhiteSpace(version)
                ? Result.Failure<string>(new Error(
                    ErrorCode.NotFound,
                    "The configured version pattern did not match the vendor response."))
                : Result.Success(version);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<string>(new Error(
                ErrorCode.ConnectionTimeout,
                $"The vendor source did not answer within {request.Timeout.TotalSeconds:0} seconds."));
        }
        catch (HttpRequestException exception)
        {
            return Result.Failure<string>(new Error(
                ErrorCode.ServiceUnavailable,
                "The vendor source could not be reached.") { Details = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<string>(new Error(
                ErrorCode.InvalidRequest,
                "The configured version pattern is invalid.") { Details = exception.Message });
        }
        catch (RegexMatchTimeoutException)
        {
            return Result.Failure<string>(new Error(
                ErrorCode.InvalidRequest,
                "The configured version pattern exceeded the safety timeout."));
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
