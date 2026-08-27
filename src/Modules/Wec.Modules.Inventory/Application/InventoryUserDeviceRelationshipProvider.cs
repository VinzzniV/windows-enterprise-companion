using Wec.Core.Contracts;
using Wec.Modules.Inventory.Domain;
using Wec.Modules.Inventory.Persistence;

namespace Wec.Modules.Inventory.Application;

internal sealed class InventoryUserDeviceRelationshipProvider(
    IHardwareSnapshotRepository repository) : IUserDeviceRelationshipProvider
{
    private const string EvidenceSource = "WEC Inventory";

    public async Task<UserDeviceRelationshipSnapshot> GetForDirectorySidAsync(
        string directorySid,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredInventoryHost> hosts = await repository.ListHostsAsync(cancellationToken);
        int evidenceCaptured = 0;
        int notCaptured = 0;
        int unavailable = 0;
        int truncated = 0;
        var devices = new List<UserLinkedDeviceEvidence>();

        foreach (StoredInventoryHost host in hosts)
        {
            CachedHardwareSnapshot? cached = await repository.GetLatestAsync(host.Host, cancellationToken);
            if (cached is null)
            {
                unavailable++;
                continue;
            }

            DeviceUserEvidence? evidence = cached.Snapshot.UserEvidence;
            if (evidence is null)
            {
                notCaptured++;
                continue;
            }

            evidenceCaptured++;
            if (evidence.InteractiveUserState == UserEvidenceSourceState.Unavailable
                || evidence.LocalProfilesState == UserEvidenceSourceState.Unavailable)
            {
                unavailable++;
            }

            if (evidence.LocalProfilesTruncated)
            {
                truncated++;
            }

            List<UserDeviceRelationshipObservation> observations = MatchObservations(
                directorySid,
                evidence,
                cached.CapturedAtUtc);
            if (observations.Count > 0)
            {
                devices.Add(new UserLinkedDeviceEvidence(host.Host, cached.CapturedAtUtc, observations));
            }
        }

        return new UserDeviceRelationshipSnapshot(
            new UserDeviceRelationshipCoverage(
                hosts.Count,
                evidenceCaptured,
                notCaptured,
                unavailable,
                truncated),
            [.. devices.OrderBy(device => device.Host, StringComparer.OrdinalIgnoreCase)]);
    }

    private static List<UserDeviceRelationshipObservation> MatchObservations(
        string directorySid,
        DeviceUserEvidence evidence,
        DateTimeOffset capturedAtUtc)
    {
        var observations = new List<UserDeviceRelationshipObservation>(2);
        if (evidence.InteractiveUserState == UserEvidenceSourceState.Available
            && string.Equals(evidence.InteractiveUser?.Sid, directorySid, StringComparison.OrdinalIgnoreCase))
        {
            observations.Add(new UserDeviceRelationshipObservation(
                UserDeviceRelationshipType.LastInteractiveUser,
                EvidenceSource,
                capturedAtUtc,
                UserDeviceRelationshipConfidence.High,
                "The directory SID was the interactive domain user when Inventory captured this device.",
                ProfileLastUseAtUtc: null));
        }

        LocalUserProfileEvidence? profile = evidence.LocalProfilesState == UserEvidenceSourceState.Available
            ? evidence.LocalProfiles?.FirstOrDefault(candidate =>
                string.Equals(candidate.Sid, directorySid, StringComparison.OrdinalIgnoreCase))
            : null;
        if (profile is not null)
        {
            observations.Add(new UserDeviceRelationshipObservation(
                UserDeviceRelationshipType.ProfilePresent,
                EvidenceSource,
                capturedAtUtc,
                UserDeviceRelationshipConfidence.Medium,
                "Inventory observed a local profile with the directory SID; this does not prove ownership.",
                profile.LastUseAtUtc));
        }

        return observations;
    }
}
