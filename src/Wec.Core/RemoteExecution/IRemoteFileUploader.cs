using Wec.Core.Results;

namespace Wec.Core.RemoteExecution;

public interface IRemoteFileUploader
{
    Task<Result<RemoteFileUploadResult>> UploadAsync(
        RemoteFileUploadRequest request,
        CancellationToken cancellationToken);
}

public sealed record RemoteFileUploadRequest(
    string LocalPath,
    string Host,
    string UserName,
    string RemotePath,
    TimeSpan ConnectTimeout,
    TimeSpan TransferTimeout,
    string? IdentityFile = null);

public sealed record RemoteFileUploadResult(
    string RemotePath,
    long Length,
    string Sha256);
