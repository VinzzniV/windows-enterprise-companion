using Wec.Core.Results;

namespace Wec.Core.Targets;

public enum ScanPhase
{
    Resolve = 0,
    Connect,
    Authenticate,
    Query,
}

/// <summary>A per-host failure inside a (multi-)target scan.</summary>
public sealed record ScanError(
    string Host,
    ScanPhase Phase,
    ErrorCode Code,
    string Message,
    string? Details = null)
{
    public static ScanError FromError(string host, Error error) => new(
        host,
        PhaseFor(error.Code),
        error.Code,
        error.Message,
        error.Details);

    private static ScanPhase PhaseFor(ErrorCode code) => code switch
    {
        ErrorCode.DnsResolutionFailed => ScanPhase.Resolve,
        ErrorCode.ConnectionTimeout or ErrorCode.WinRmUnavailable => ScanPhase.Connect,
        ErrorCode.AuthenticationFailed or ErrorCode.AccessDenied => ScanPhase.Authenticate,
        _ => ScanPhase.Query,
    };
}
