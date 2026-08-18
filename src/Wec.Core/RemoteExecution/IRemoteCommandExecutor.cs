using Wec.Core.Results;

namespace Wec.Core.RemoteExecution;

/// <summary>
/// Executes one non-interactive command on a remote host. Authentication is
/// delegated to the platform SSH client (agent or key file); passwords never
/// cross this boundary.
/// </summary>
public interface IRemoteCommandExecutor
{
    Task<Result<RemoteCommandResult>> ExecuteAsync(
        RemoteCommandRequest request,
        CancellationToken cancellationToken);
}

public sealed record RemoteCommandRequest(
    string Host,
    string UserName,
    string Command,
    TimeSpan ConnectTimeout,
    TimeSpan CommandTimeout,
    string? IdentityFile = null);

public sealed record RemoteCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration);
