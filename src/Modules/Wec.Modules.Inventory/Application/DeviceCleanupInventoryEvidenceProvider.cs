using Wec.Core.Contracts;
using Wec.Modules.Inventory.Domain;
using Wec.Modules.Inventory.Persistence;

namespace Wec.Modules.Inventory.Application;

internal sealed class DeviceCleanupInventoryEvidenceProvider(
    IHardwareSnapshotRepository repository) : IDeviceCleanupInventoryEvidenceProvider
{
    public async Task<DeviceCleanupInventoryEvidence?> GetLatestAsync(
        string host,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        CachedHardwareSnapshot? cached = await repository.GetLatestAsync(
            host.Trim().ToUpperInvariant(),
            cancellationToken);
        return cached is null ? null : Project(host.Trim(), cached);
    }

    internal static DeviceCleanupInventoryEvidence Project(
        string host,
        CachedHardwareSnapshot cached)
    {
        DeviceUserEvidence? evidence = cached.Snapshot.UserEvidence;
        if (evidence is null)
        {
            return new DeviceCleanupInventoryEvidence(
                host,
                cached.CapturedAtUtc,
                DeviceCleanupUserEvidenceAvailability.NotCaptured,
                "This stored Inventory snapshot predates approved user/device evidence capture.",
                []);
        }

        var observations = new List<DeviceCleanupUserObservation>();
        if (evidence.InteractiveUserState == UserEvidenceSourceState.Available
            && evidence.InteractiveUser is not null)
        {
            observations.Add(new DeviceCleanupUserObservation(
                "LastInteractiveUser",
                evidence.InteractiveUser.Sid,
                $"{evidence.InteractiveUser.Domain}\\{evidence.InteractiveUser.AccountName}",
                cached.CapturedAtUtc,
                null,
                "High",
                "Inventory observed this directory SID as the interactive user; this is not an ownership claim."));
        }

        if (evidence.LocalProfilesState == UserEvidenceSourceState.Available)
        {
            observations.AddRange((evidence.LocalProfiles ?? []).Select(profile =>
                new DeviceCleanupUserObservation(
                    "ProfilePresent",
                    profile.Sid,
                    null,
                    cached.CapturedAtUtc,
                    profile.LastUseAtUtc,
                    "Medium",
                    "Inventory observed a local profile with this SID; profile presence does not prove ownership.")));
        }

        DeviceCleanupUserEvidenceAvailability availability = Availability(evidence);
        return new DeviceCleanupInventoryEvidence(
            host,
            cached.CapturedAtUtc,
            availability,
            Explanation(availability, observations.Count),
            [.. observations
                .OrderByDescending(observation => observation.RelationshipType == "LastInteractiveUser")
                .ThenByDescending(observation => observation.ProfileLastUseAtUtc)
                .ThenBy(observation => observation.Sid, StringComparer.Ordinal)]);
    }

    private static DeviceCleanupUserEvidenceAvailability Availability(DeviceUserEvidence evidence)
    {
        if (evidence.LocalProfilesTruncated)
        {
            return DeviceCleanupUserEvidenceAvailability.Truncated;
        }

        int unavailable = (evidence.InteractiveUserState == UserEvidenceSourceState.Unavailable ? 1 : 0)
            + (evidence.LocalProfilesState == UserEvidenceSourceState.Unavailable ? 1 : 0);
        if (unavailable == 2)
        {
            return DeviceCleanupUserEvidenceAvailability.Unavailable;
        }

        if (unavailable == 1
            || evidence.InteractiveUserState == UserEvidenceSourceState.NotCaptured
            || evidence.LocalProfilesState == UserEvidenceSourceState.NotCaptured)
        {
            return DeviceCleanupUserEvidenceAvailability.Partial;
        }

        return DeviceCleanupUserEvidenceAvailability.Available;
    }

    private static string Explanation(
        DeviceCleanupUserEvidenceAvailability availability,
        int observationCount) => availability switch
    {
        DeviceCleanupUserEvidenceAvailability.Available =>
            $"Stored Inventory exposed {observationCount} approved user/device observation(s).",
        DeviceCleanupUserEvidenceAvailability.Partial =>
            $"Stored Inventory exposed {observationCount} observation(s), but one evidence source was not captured or unavailable.",
        DeviceCleanupUserEvidenceAvailability.Truncated =>
            $"Stored Inventory exposed {observationCount} observation(s), but the local-profile list was truncated.",
        _ => "Stored Inventory could not collect approved user/device evidence.",
    };
}
