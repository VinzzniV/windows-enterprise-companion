using Wec.Core.Privileges;

namespace Wec.Modules.Diagnostics.Domain;

public enum DiagnosticStatus
{
    Pass = 0,
    Warning = 1,
    Fail = 2,
    NotRun = 3,
}

public enum DiagnosticCategory
{
    Network = 0,
    Domain = 1,
    TimeSynchronization = 2,
    EventLog = 3,
    Services = 4,
}

public sealed record DiagnosticResult(
    string DiagnosticId,
    string Title,
    DiagnosticStatus Status,
    DiagnosticCategory Category,
    string AffectedResource,
    IReadOnlyDictionary<string, string> Evidence,
    IReadOnlyList<string> SuggestedNextSteps,
    PrivilegeLevel? RequiredPrivilege,
    DateTimeOffset CapturedAtUtc);

public sealed record DiagnosticRunResult(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<DiagnosticResult> Results);
