using Wec.Core.Results;

namespace Wec.Core.RemoteExecution;

/// <summary>
/// Resolves one vendor artifact from a bounded HTTPS response and uploads it
/// to a prevalidated temporary path through the platform OpenSSH client.
/// </summary>
public interface IRemoteArtifactStager
{
    Task<Result<RemoteArtifactStageResult>> StageAsync(
        RemoteArtifactStageRequest request,
        CancellationToken cancellationToken);

    Task<Result<RemoteArtifactTransferResult>> TransferAsync(
        RemoteArtifactTransferRequest request,
        CancellationToken cancellationToken);
}

public sealed record RemoteArtifactStageRequest(
    Uri ReleaseUrl,
    string ArtifactUrlPattern,
    long MaximumArtifactBytes,
    string Host,
    string UserName,
    string RemotePath,
    TimeSpan ConnectTimeout,
    TimeSpan TransferTimeout,
    string? IdentityFile = null);

public sealed record RemoteArtifactStageResult(
    Uri ArtifactUrl,
    string RemotePath,
    long Length,
    string Sha256);

public sealed record RemoteArtifactTransferRequest(
    string SourceHost,
    string TargetHost,
    string UserName,
    string RemotePath,
    TimeSpan ConnectTimeout,
    TimeSpan TransferTimeout,
    string? IdentityFile = null);

public sealed record RemoteArtifactTransferResult(
    string SourceHost,
    string TargetHost,
    string RemotePath);
