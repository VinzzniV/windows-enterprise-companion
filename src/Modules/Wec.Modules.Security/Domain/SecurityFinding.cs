using Wec.Core.Privileges;

namespace Wec.Modules.Security.Domain;

public sealed record SecurityFinding(
    string FindingId,
    string Title,
    string Description,
    FindingSeverity Severity,
    FindingCategory Category,
    string AffectedResource,
    IReadOnlyDictionary<string, string> Evidence,
    string Recommendation,
    PrivilegeLevel? RequiredPrivilege,
    DateTimeOffset CapturedAtUtc);
