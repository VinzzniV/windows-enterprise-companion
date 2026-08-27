using Wec.Core.Results;

namespace Wec.Core.Contracts;

public sealed record DeviceCleanupEvidenceQuery(
    HygieneActionDirectoryConnection? ActiveDirectory = null,
    HygieneActionKasperskyConnection? Kaspersky = null,
    string? OperationId = null,
    bool Force = false);

public sealed record DeviceCleanupAdEvidence(
    bool Exists,
    bool? Enabled,
    string? OperatingSystem,
    string? DistinguishedName,
    string? OrganizationalUnit,
    DateTimeOffset? LastLogonAtUtc);

public sealed record DeviceCleanupKasperskyEvidence(
    bool Exists,
    DateTimeOffset? LastSeenAtUtc,
    string? AdministrationGroup);

public sealed record DeviceCleanupOpsiEvidence(
    bool Exists,
    DateTimeOffset? LastSeenAtUtc,
    string? DepotId);

public sealed record DeviceCleanupNessusEvidence(
    bool Exists,
    DateTimeOffset? LastCompletedScanAtUtc);

public sealed record DeviceCleanupFindingEvidence(
    string Code,
    string Severity,
    string Message);

public sealed record DeviceCleanupSubjectEvidence(
    string SubjectKey,
    string Host,
    string HygieneStatus,
    DeviceCleanupAdEvidence ActiveDirectory,
    DeviceCleanupKasperskyEvidence Kaspersky,
    DeviceCleanupOpsiEvidence Opsi,
    DeviceCleanupNessusEvidence Nessus,
    IReadOnlyList<DeviceCleanupFindingEvidence> Findings);

public sealed record DeviceCleanupEvidenceSnapshot(
    DateTimeOffset AssessedAtUtc,
    IReadOnlyList<ActionEvidenceSourceState> Sources,
    IReadOnlyList<DeviceCleanupSubjectEvidence> Subjects);

/// <summary>
/// Read-only source facts for the stale-device cleanup assistant. The provider
/// owns no decision state and reuses the request-bound hygiene snapshot.
/// </summary>
public interface IDeviceCleanupEvidenceProvider
{
    Task<Result<DeviceCleanupEvidenceSnapshot>> LoadAsync(
        DeviceCleanupEvidenceQuery query,
        CancellationToken cancellationToken);
}

public enum DeviceCleanupUserEvidenceAvailability
{
    Available = 0,
    Partial,
    NotCaptured,
    Unavailable,
    Truncated,
}

public sealed record DeviceCleanupUserObservation(
    string RelationshipType,
    string Sid,
    string? AccountDisplay,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset? ProfileLastUseAtUtc,
    string Confidence,
    string Explanation);

public sealed record DeviceCleanupInventoryEvidence(
    string Host,
    DateTimeOffset CapturedAtUtc,
    DeviceCleanupUserEvidenceAvailability UserEvidenceAvailability,
    string UserEvidenceExplanation,
    IReadOnlyList<DeviceCleanupUserObservation> UserObservations);

/// <summary>
/// Latest stored Inventory and approved user/device observations for one
/// cleanup subject. Reading this contract never starts an Inventory scan.
/// </summary>
public interface IDeviceCleanupInventoryEvidenceProvider
{
    Task<DeviceCleanupInventoryEvidence?> GetLatestAsync(
        string host,
        CancellationToken cancellationToken);
}
