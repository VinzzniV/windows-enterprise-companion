namespace Wec.Core.Contracts;

public enum UserDeviceRelationshipType
{
    LastInteractiveUser = 0,
    ProfilePresent,
}

public enum UserDeviceRelationshipConfidence
{
    High = 0,
    Medium,
}

public sealed record UserDeviceRelationshipObservation(
    UserDeviceRelationshipType RelationshipType,
    string Source,
    DateTimeOffset ObservedAtUtc,
    UserDeviceRelationshipConfidence Confidence,
    string Explanation,
    DateTimeOffset? ProfileLastUseAtUtc);

public sealed record UserLinkedDeviceEvidence(
    string Host,
    DateTimeOffset InventoryCapturedAtUtc,
    IReadOnlyList<UserDeviceRelationshipObservation> Observations);

public sealed record UserDeviceRelationshipCoverage(
    int StoredDeviceCount,
    int EvidenceCapturedDeviceCount,
    int NotCapturedDeviceCount,
    int UnavailableDeviceCount,
    int TruncatedDeviceCount);

public sealed record UserDeviceRelationshipSnapshot(
    UserDeviceRelationshipCoverage Coverage,
    IReadOnlyList<UserLinkedDeviceEvidence> Devices);

/// <summary>
/// Read-only, SID-matched Inventory evidence. It exposes no account-name or
/// host-name inference and no user-activity history (ADR 0019).
/// </summary>
public interface IUserDeviceRelationshipProvider
{
    Task<UserDeviceRelationshipSnapshot> GetForDirectorySidAsync(
        string directorySid,
        CancellationToken cancellationToken);
}
