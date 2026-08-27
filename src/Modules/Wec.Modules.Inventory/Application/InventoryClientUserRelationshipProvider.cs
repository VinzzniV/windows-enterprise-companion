using Wec.Core.Contracts;
using Wec.Core.Targets;
using Wec.Modules.Inventory.Domain;
using Wec.Modules.Inventory.Persistence;

namespace Wec.Modules.Inventory.Application;

internal sealed class InventoryClientUserRelationshipProvider(
    IHardwareSnapshotRepository repository) : IClientUserRelationshipProvider
{
    private const string EvidenceSource = "WEC Inventory";

    public async Task<ClientUserRelationshipSnapshot?> GetLatestAsync(
        string? host,
        CancellationToken cancellationToken)
    {
        string cacheKey = host is null
            ? ScanTarget.Local.CacheKey
            : ScanTarget.Remote(host).CacheKey;
        CachedHardwareSnapshot? cached = await repository.GetLatestAsync(cacheKey, cancellationToken);
        return cached is null ? null : Project(cached);
    }

    internal static ClientUserRelationshipSnapshot Project(CachedHardwareSnapshot cached)
    {
        DeviceUserEvidence? evidence = cached.Snapshot.UserEvidence;
        if (evidence is null)
        {
            return new ClientUserRelationshipSnapshot(
                cached.CapturedAtUtc,
                ClientUserEvidenceAvailability.NotCaptured,
                "This stored Inventory snapshot predates approved user/device evidence capture.",
                UnresolvedProfileCount: 0,
                Observations: []);
        }

        List<ClientObservedUserEvidence> observations = [];
        if (evidence.InteractiveUserState == UserEvidenceSourceState.Available
            && evidence.InteractiveUser is not null)
        {
            observations.Add(new ClientObservedUserEvidence(
                evidence.InteractiveUser.Sid,
                $"{evidence.InteractiveUser.Domain}\\{evidence.InteractiveUser.AccountName}",
                UserDeviceRelationshipType.LastInteractiveUser,
                EvidenceSource,
                cached.CapturedAtUtc,
                UserDeviceRelationshipConfidence.High,
                "Inventory observed this directory SID as the interactive user; this is not an ownership claim."));
        }

        string? interactiveSid = evidence.InteractiveUser?.Sid;
        int unresolvedProfiles = evidence.LocalProfilesState == UserEvidenceSourceState.Available
            ? (evidence.LocalProfiles ?? []).Count(profile =>
                !string.Equals(profile.Sid, interactiveSid, StringComparison.OrdinalIgnoreCase))
            : 0;
        ClientUserEvidenceAvailability availability = Availability(evidence);
        return new ClientUserRelationshipSnapshot(
            cached.CapturedAtUtc,
            availability,
            Explanation(availability, observations.Count, unresolvedProfiles),
            unresolvedProfiles,
            observations);
    }

    private static ClientUserEvidenceAvailability Availability(DeviceUserEvidence evidence)
    {
        if (evidence.LocalProfilesTruncated)
        {
            return ClientUserEvidenceAvailability.Truncated;
        }

        int unavailable = (evidence.InteractiveUserState == UserEvidenceSourceState.Unavailable ? 1 : 0)
            + (evidence.LocalProfilesState == UserEvidenceSourceState.Unavailable ? 1 : 0);
        if (unavailable == 2)
        {
            return ClientUserEvidenceAvailability.Unavailable;
        }

        if (unavailable == 1
            || evidence.InteractiveUserState == UserEvidenceSourceState.NotCaptured
            || evidence.LocalProfilesState == UserEvidenceSourceState.NotCaptured)
        {
            return ClientUserEvidenceAvailability.Partial;
        }

        return ClientUserEvidenceAvailability.Available;
    }

    private static string Explanation(
        ClientUserEvidenceAvailability availability,
        int namedObservationCount,
        int unresolvedProfileCount) => availability switch
    {
        ClientUserEvidenceAvailability.Available =>
            $"Stored Inventory exposed {namedObservationCount} named observation(s) and {unresolvedProfileCount} unresolved local profile(s).",
        ClientUserEvidenceAvailability.Partial =>
            $"Stored Inventory exposed {namedObservationCount} named observation(s), but one user-evidence source was unavailable or not captured.",
        ClientUserEvidenceAvailability.Truncated =>
            $"Stored Inventory exposed {namedObservationCount} named observation(s), but the local-profile evidence was truncated.",
        _ => "Stored Inventory could not collect approved user/device evidence.",
    };
}
