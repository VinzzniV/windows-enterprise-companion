using Wec.Core.Contracts;

namespace Wec.Modules.DeviceCleanup.Application;

public enum DeviceCleanupClassification
{
    PotentialCleanup = 0,
    Review,
    InsufficientEvidence,
    NoCleanupSignal,
}

public sealed record DeviceCleanupCandidate(
    string SubjectKey,
    string Host,
    string? Description,
    string? DescriptionSource,
    DeviceCleanupClassification Classification,
    string ClassificationExplanation,
    bool? ActiveDirectoryExists,
    bool? ActiveDirectoryEnabled,
    DateTimeOffset? ActiveDirectoryLastLogonAtUtc,
    bool? KasperskyExists,
    DateTimeOffset? KasperskyLastSeenAtUtc,
    bool? OpsiExists,
    DateTimeOffset? OpsiLastSeenAtUtc,
    bool? NessusExists,
    DateTimeOffset? NessusLastScanAtUtc,
    bool InventoryExists,
    DateTimeOffset? InventoryCapturedAtUtc,
    int RelevantFindingCount)
{
    public bool CanTargetWindows { get; init; } = true;
}

internal sealed record DeviceCleanupExportSnapshot(
    IReadOnlyList<DeviceCleanupCandidate> Candidates,
    DateTimeOffset AssessedAtUtc,
    IReadOnlyList<ActionEvidenceSourceState> Sources,
    bool SubjectsTruncated);

internal sealed record DeviceCleanupExportQuery(
    HygieneActionDirectoryConnection? ActiveDirectory,
    HygieneActionKasperskyConnection? Kaspersky,
    string? OperationId,
    string? Search,
    bool IncludeWithoutSignals);

public sealed record DeviceCleanupSourceFact(
    string Source,
    ActionEvidenceAvailability Coverage,
    bool? Exists,
    string State,
    DateTimeOffset? ObservedAtUtc,
    string Explanation);

public sealed record DeviceCleanupAssessment(
    DeviceCleanupCandidate Candidate,
    IReadOnlyList<DeviceCleanupSourceFact> Sources,
    IReadOnlyList<DeviceCleanupFindingEvidence> Findings,
    DeviceCleanupUserEvidenceAvailability UserEvidenceAvailability,
    string UserEvidenceExplanation,
    IReadOnlyList<DeviceCleanupUserObservation> UserObservations);

public sealed record DeviceCleanupPage(
    IReadOnlyList<DeviceCleanupCandidate> Candidates,
    int Total,
    int Page,
    int PageSize,
    DateTimeOffset AssessedAtUtc,
    IReadOnlyList<ActionEvidenceSourceState> Sources,
    DeviceCleanupAssessment? SelectedAssessment,
    bool SubjectsTruncated);

public sealed record ListDeviceCleanupCandidatesRequest(
    HygieneActionDirectoryConnection? ActiveDirectory = null,
    HygieneActionKasperskyConnection? Kaspersky = null,
    string? OperationId = null,
    bool Force = false,
    string? Search = null,
    string? SelectedHost = null,
    bool IncludeWithoutSignals = false,
    int Page = 1,
    int PageSize = 25);
