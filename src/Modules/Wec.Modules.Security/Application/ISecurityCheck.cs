using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Application;

/// <summary>
/// One read-only security check. Expected operational failures (WMI
/// unavailable, missing privileges, unsupported remote operation) must be
/// converted into findings by the check itself — a check that could not run
/// has to stay visible, never silently drop out (ADR 0002, ADR 0007).
/// Exceptions escaping a check are bugs.
/// </summary>
public interface ISecurityCheck
{
    string CheckId { get; }

    Task<IReadOnlyList<SecurityFinding>> EvaluateAsync(
        SecurityScanContext context,
        CancellationToken cancellationToken);
}
