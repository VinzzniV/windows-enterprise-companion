using Wec.Core.Results;

namespace Wec.Core.SoftwareUpdates;

public sealed record VendorVersionRequest(Uri SourceUrl, string VersionPattern, TimeSpan Timeout);

/// <summary>Reads a vendor release page or API without knowing package-management details.</summary>
public interface IVendorVersionClient
{
    Task<Result<string>> GetLatestVersionAsync(
        VendorVersionRequest request,
        CancellationToken cancellationToken);
}
