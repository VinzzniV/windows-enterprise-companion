using Wec.Core.Results;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Application.Checks;

internal static class CheckFindings
{
    /// <summary>
    /// Uniform "check only runs on the local machine" finding for remote
    /// targets (ADR 0007: no silent local fallback, no silent skip).
    /// </summary>
    public static SecurityFinding LocalOnly(
        string checkId,
        string title,
        FindingCategory category,
        string affectedResource,
        string host,
        DateTimeOffset capturedAtUtc) => new(
        FindingId: $"{checkId}-LOCAL-ONLY",
        Title: title,
        Description:
            "This check reads data that is only accessible on the machine WEC runs on "
            + "(registry or local security APIs). It was skipped for the remote target.",
        Severity: FindingSeverity.Info,
        Category: category,
        AffectedResource: affectedResource,
        Evidence: new Dictionary<string, string>
        {
            ["errorCode"] = ErrorCode.UnsupportedRemoteOperation.ToString(),
            ["host"] = host,
        },
        Recommendation: "Run WEC directly on this machine to include the check.",
        RequiredPrivilege: null,
        CapturedAtUtc: capturedAtUtc);

    /// <summary>
    /// Uniform "check could not run" finding: conservative INFO severity —
    /// an unknown state is reported, never alarmed and never hidden (ADR 0002).
    /// </summary>
    public static SecurityFinding NotRun(
        string checkId,
        string title,
        FindingCategory category,
        string affectedResource,
        string recommendation,
        Error error,
        DateTimeOffset capturedAtUtc) => new(
        FindingId: $"{checkId}-NOT-RUN",
        Title: title,
        Description:
            "The check could not read the required data. The state is unknown, "
            + "not necessarily bad — investigate why the read failed.",
        Severity: FindingSeverity.Info,
        Category: category,
        AffectedResource: affectedResource,
        Evidence: new Dictionary<string, string>
        {
            ["errorCode"] = error.Code.ToString(),
            ["errorMessage"] = error.Message,
        },
        Recommendation: recommendation,
        RequiredPrivilege: error.RequiredPrivilege,
        CapturedAtUtc: capturedAtUtc);
}
