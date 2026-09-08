using Wec.Core.Contracts;

namespace Wec.Modules.ActionCenter.Application;

public enum ActionCenterSeverity
{
    Critical = 0,
    High,
    Warning,
    Medium,
    Low,
    Information,
    Unknown,
}

public enum ActionCenterSortField
{
    Severity = 0,
    Device,
    Source,
    EvidenceAge,
    Problem,
}

public enum ActionCenterSortDirection
{
    Ascending = 0,
    Descending,
}

public sealed record ActionCenterWorkItem(
    string Id,
    string SubjectType,
    string SubjectKey,
    string Device,
    Guid? UserObjectId,
    string? UserDisplayName,
    string ProblemCode,
    string Problem,
    string Explanation,
    string Source,
    ActionCenterSeverity Severity,
    DateTimeOffset? EvidenceAtUtc,
    DateTimeOffset AssessedAtUtc,
    int? EvidenceAgeDays,
    ActionEvidenceAvailability Coverage,
    string Reliability,
    string CoverageExplanation,
    string RecommendedAction,
    string Href);

public sealed record ActionCenterSummary(
    int Total,
    int AffectedDevices,
    int Critical,
    int High,
    int Warning,
    int UnknownCoverage);

public sealed record ActionCenterPage(
    IReadOnlyList<ActionCenterWorkItem> Items,
    int Total,
    int Page,
    int PageSize,
    ActionCenterSummary Summary,
    long SnapshotRevision,
    DateTimeOffset AssessedAtUtc,
    IReadOnlyList<ActionEvidenceSourceState> Sources,
    bool ItemsTruncated);

public sealed record ListActionCenterItemsRequest(
    HygieneActionDirectoryConnection? ActiveDirectory = null,
    HygieneActionKasperskyConnection? Kaspersky = null,
    string? OperationId = null,
    bool Force = false,
    string? Search = null,
    ActionCenterSeverity? Severity = null,
    string? Source = null,
    int Page = 1,
    int PageSize = 25,
    ActionCenterSortField SortField = ActionCenterSortField.Severity,
    ActionCenterSortDirection SortDirection = ActionCenterSortDirection.Ascending);
