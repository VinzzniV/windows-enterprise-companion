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
}
