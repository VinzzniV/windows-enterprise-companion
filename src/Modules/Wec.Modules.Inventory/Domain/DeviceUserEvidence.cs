namespace Wec.Modules.Inventory.Domain;

public enum UserEvidenceSourceState
{
    NotCaptured = 0,
    Available,
    Unavailable,
}

public sealed record UserEvidenceCaptureError(string Code, string Message);

public sealed record InteractiveDomainUserEvidence(
    string Sid,
    string Domain,
    string AccountName);

public sealed record LocalUserProfileEvidence(
    string Sid,
    DateTimeOffset? LastUseAtUtc);

/// <summary>
/// Bounded personal-data allowlist from an explicitly started Inventory scan.
/// Null on older snapshots means that this section was not captured.
/// </summary>
public sealed record DeviceUserEvidence(
    UserEvidenceSourceState InteractiveUserState,
    InteractiveDomainUserEvidence? InteractiveUser,
    UserEvidenceCaptureError? InteractiveUserError,
    UserEvidenceSourceState LocalProfilesState,
    IReadOnlyList<LocalUserProfileEvidence>? LocalProfiles,
    UserEvidenceCaptureError? LocalProfilesError,
    bool LocalProfilesTruncated);
