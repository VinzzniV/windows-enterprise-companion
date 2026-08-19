using Wec.Core.Results;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Application.Checks;

internal static class CheckFindings
{
    /// <summary>
    /// Uniform not-applicable execution outcome for checks that only run on
    /// the local machine (ADR 0007: no silent local fallback or skip).
    /// </summary>
    public static SecurityCheckResult LocalOnly(
        string checkId,
        string host,
        string subject) => SecurityCheckResult.DidNotRun(
        checkId,
        new Error(
            ErrorCode.UnsupportedRemoteOperation,
            $"{subject} is only available locally and was not evaluated on {host}."));

    /// <summary>
    /// Uniform incomplete execution outcome. Unknown provider state is kept
    /// separate from findings and therefore can never be interpreted as PASS.
    /// </summary>
    public static SecurityCheckResult NotRun(
        string checkId,
        Error error) => SecurityCheckResult.DidNotRun(checkId, error);
}
