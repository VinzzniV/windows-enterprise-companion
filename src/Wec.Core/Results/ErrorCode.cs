namespace Wec.Core.Results;

public enum ErrorCode
{
    InternalError = 0,
    AccessDenied,
    NotFound,
    WmiUnavailable,
    InvalidRequest,
    UnknownAction,
    NetworkProbeFailed,
    EventLogUnavailable,
    FileWriteFailed,
    DirectoryUnavailable,
    DnsResolutionFailed,
    ConnectionTimeout,
    AuthenticationFailed,
    WinRmUnavailable,
    UnsupportedRemoteOperation,
    ServiceUnavailable,
    RemoteCommandFailed,
}
