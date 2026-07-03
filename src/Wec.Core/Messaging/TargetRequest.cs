using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Core.Messaging;

/// <summary>
/// Bridge payload fragment shared by every action that can run against a
/// local or remote machine. A null/empty host means the local machine.
/// </summary>
public sealed record TargetRequest(
    string? Host = null,
    string? UserName = null,
    string? Domain = null,
    string? Password = null)
{
    public bool IsLocal => string.IsNullOrWhiteSpace(Host);

    public ScanTarget ToScanTarget() => IsLocal ? ScanTarget.Local : ScanTarget.Remote(Host!);

    public Result<ScanCredentials> ToScanCredentials()
    {
        if (string.IsNullOrWhiteSpace(UserName))
        {
            return Result.Success(ScanCredentials.CurrentUser);
        }

        if (Password is null)
        {
            return Result.Failure<ScanCredentials>(new Error(
                ErrorCode.InvalidRequest,
                "Explicit credentials require a password."));
        }

        if (IsLocal)
        {
            return Result.Failure<ScanCredentials>(new Error(
                ErrorCode.UnsupportedRemoteOperation,
                "Explicit credentials are only supported for remote targets."));
        }

        return Result.Success(ScanCredentials.Explicit(UserName, Domain, Password));
    }
}
