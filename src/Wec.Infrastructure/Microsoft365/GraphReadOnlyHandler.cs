using System.Net;
using System.Net.Http;

namespace Wec.Infrastructure.Microsoft365;

internal sealed class GraphReadOnlyHandler(Microsoft365Options options, HttpMessageHandler innerHandler)
    : DelegatingHandler(innerHandler)
{
    internal static bool IsAllowed(Uri? uri) => uri is { IsAbsoluteUri: true }
        && uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("graph.microsoft.com", StringComparison.OrdinalIgnoreCase)
        && uri.Port == 443 && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0
        && uri.AbsolutePath.StartsWith("/v1.0/", StringComparison.Ordinal);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get || !IsAllowed(request.RequestUri))
        {
            throw new InvalidDataException("Graph read-only transport rejected the request.");
        }

        for (int attempt = 0; ; attempt++)
        {
            using var copy = new HttpRequestMessage(HttpMethod.Get, request.RequestUri);
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            {
                copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            HttpResponseMessage response = await base.SendAsync(copy, cancellationToken).ConfigureAwait(false);
            bool retryable = response.StatusCode is HttpStatusCode.TooManyRequests
                or HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout;
            TimeSpan delay = response.Headers.RetryAfter?.Delta
                ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            if (retryable && attempt < options.MaximumRetries
                && delay <= TimeSpan.FromSeconds(options.MaximumRetryDelaySeconds))
            {
                response.Dispose();
                await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                await response.Content.LoadIntoBufferAsync(options.MaximumResponseBytes, cancellationToken).ConfigureAwait(false);
                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
    }
}
