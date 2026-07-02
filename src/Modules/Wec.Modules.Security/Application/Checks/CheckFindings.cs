using Wec.Core.Results;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Application.Checks;

internal static class CheckFindings
{
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
