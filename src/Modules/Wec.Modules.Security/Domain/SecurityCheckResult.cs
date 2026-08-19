using Wec.Core.Privileges;
using Wec.Core.Results;

namespace Wec.Modules.Security.Domain;

/// <summary>
/// User-safe information about why a security check did not produce complete
/// coverage. Provider details and exception text are deliberately excluded.
/// </summary>
public sealed record SecurityCheckFailure(
    ErrorCode Code,
    string Message,
    PrivilegeLevel? RequiredPrivilege)
{
    public static SecurityCheckFailure FromError(Error error) => new(
        error.Code,
        error.Message,
        error.RequiredPrivilege);
}

/// <summary>
/// Technical execution outcome of one security check. Findings describe only
/// observed conditions; execution gaps are represented by <see cref="Status"/>
/// and <see cref="Failure"/> instead of informational pseudo-findings.
/// </summary>
public sealed record SecurityCheckResult(
    string CheckId,
    CheckStatus Status,
    IReadOnlyList<SecurityFinding> Findings,
    SecurityCheckFailure? Failure = null)
{
    public static SecurityCheckResult Succeeded(
        string checkId,
        IReadOnlyList<SecurityFinding>? findings = null) =>
        new(checkId, CheckStatus.Succeeded, findings ?? []);

    public static SecurityCheckResult DidNotRun(
        string checkId,
        Error error,
        IReadOnlyList<SecurityFinding>? observedFindings = null)
    {
        CheckStatus status = error.Code switch
        {
            ErrorCode.UnsupportedRemoteOperation => CheckStatus.NotApplicable,
            ErrorCode.AccessDenied => CheckStatus.RequiresElevation,
            _ when error.RequiredPrivilege is not null => CheckStatus.RequiresElevation,
            _ => CheckStatus.Failed,
        };

        return new SecurityCheckResult(
            checkId,
            status,
            observedFindings ?? [],
            SecurityCheckFailure.FromError(error));
    }
}

/// <summary>Derived coverage of the persisted check outcomes of one scan.</summary>
public sealed record SecurityCoverage(
    bool IsKnown,
    int TotalChecks,
    int SucceededChecks,
    int FailedChecks,
    int RequiresElevationChecks,
    int NotApplicableChecks)
{
    public const int CurrentVersion = 1;

    public int ApplicableChecks => TotalChecks - NotApplicableChecks;

    public bool IsComplete => IsKnown && TotalChecks > 0 && SucceededChecks == ApplicableChecks;

    public static SecurityCoverage From(
        int? coverageVersion,
        IReadOnlyList<SecurityCheckResult> checkResults)
    {
        if (coverageVersion is null)
        {
            return new SecurityCoverage(false, 0, 0, 0, 0, 0);
        }

        return new SecurityCoverage(
            true,
            checkResults.Count,
            checkResults.Count(result => result.Status == CheckStatus.Succeeded),
            checkResults.Count(result => result.Status == CheckStatus.Failed),
            checkResults.Count(result => result.Status == CheckStatus.RequiresElevation),
            checkResults.Count(result => result.Status == CheckStatus.NotApplicable));
    }
}
