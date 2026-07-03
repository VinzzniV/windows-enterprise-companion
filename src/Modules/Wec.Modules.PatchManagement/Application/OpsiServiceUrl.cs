using Wec.Core.Results;

namespace Wec.Modules.PatchManagement.Application;

internal static class OpsiServiceUrl
{
    /// <summary>
    /// Accepts what an admin types — "opsi.kauth.local",
    /// "opsi.kauth.local:4447" or a full https URL — and normalizes it to
    /// the opsiconfd service URL. A missing scheme becomes https; a missing
    /// port becomes <paramref name="defaultPort"/>.
    /// </summary>
    internal static Result<Uri> Normalize(string? input, int defaultPort)
    {
        string trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return Result.Failure<Uri>(new Error(
                ErrorCode.InvalidRequest, "The opsi server name is required."));
        }

        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = "https://" + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? url)
            || url.Scheme is not ("https" or "http")
            || url.Host.Length == 0)
        {
            return Result.Failure<Uri>(new Error(
                ErrorCode.InvalidRequest,
                $"'{input}' is not a valid opsi server — expected a host name like 'opsi.example.local' "
                + "or a full https URL."));
        }

        // Uri reports the scheme default when no port was typed; only then
        // apply the opsiconfd default
        string authority = trimmed[(trimmed.IndexOf("://", StringComparison.Ordinal) + 3)..].Split('/')[0];
        if (!authority.Contains(':', StringComparison.Ordinal))
        {
            url = new UriBuilder(url) { Port = defaultPort }.Uri;
        }

        return Result.Success(url);
    }
}
