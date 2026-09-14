using Microsoft.Extensions.Options;
using Wec.Core.Contracts;
using Wec.Modules.Inventory.Domain;
using Wec.Modules.Inventory.Persistence;

namespace Wec.Modules.Inventory.Application;

internal sealed class InventoryUserDeviceRelationshipProvider(
    IHardwareSnapshotRepository repository,
    IOptions<InventoryOptions> options) : IUserDeviceRelationshipProvider
{
    private const string EvidenceSource = "WEC Inventory";

    public async Task<UserDeviceRelationshipSnapshot> GetForDirectorySidAsync(
        string directorySid, CancellationToken cancellationToken)
    {
        StoredInventoryUserEvidenceBatch batch = await repository.GetLatestUserEvidenceBatchAsync(
            options.Value.MaxStoredEvidenceRecords, cancellationToken);
        var captured = new HashSet<string>(StringComparer.Ordinal);
        var notCaptured = new HashSet<string>(StringComparer.Ordinal);
        var unavailable = new HashSet<string>(StringComparer.Ordinal);
        var truncated = new HashSet<string>(StringComparer.Ordinal);
        var devices = new List<UserLinkedDeviceEvidence>();
        IGrouping<string, StoredInventoryUserEvidence>[] hosts = batch.Records.GroupBy(record => record.Host, StringComparer.Ordinal).ToArray();
        foreach (IGrouping<string, StoredInventoryUserEvidence> host in hosts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var observations = new List<UserDeviceRelationshipObservation>();
            foreach (StoredInventoryUserEvidence record in host)
            {
                if (!record.Readable) { unavailable.Add(host.Key); continue; }
                DeviceUserEvidence? evidence = record.Evidence;
                if (evidence is null) { notCaptured.Add(host.Key); continue; }
                captured.Add(host.Key);
                if (evidence.InteractiveUserState == UserEvidenceSourceState.Unavailable
                    || evidence.LocalProfilesState == UserEvidenceSourceState.Unavailable) { unavailable.Add(host.Key); }
                if (evidence.InteractiveUserState == UserEvidenceSourceState.NotCaptured
                    || evidence.LocalProfilesState == UserEvidenceSourceState.NotCaptured) { notCaptured.Add(host.Key); }
                if (evidence.LocalProfilesTruncated) { truncated.Add(host.Key); }
                observations.AddRange(MatchObservations(directorySid, evidence, record.CapturedAtUtc));
            }
            if (observations.Count > 0)
            {
                devices.Add(new(host.Key, host.Max(record => record.CapturedAtUtc), observations.Distinct().ToArray()));
            }
        }
        return new(new(batch.StoredHostCount, captured.Count, notCaptured.Count, unavailable.Count, truncated.Count)
        {
            EvaluatedDeviceCount = hosts.Length,
            WorkingSetTruncated = batch.Records.Count < batch.LatestRecordCount,
            MultipleLatestSnapshotDeviceCount = hosts.Count(host => host.Skip(1).Any()),
        }, devices.OrderBy(device => device.Host, StringComparer.Ordinal).ToArray());
    }

    private static IEnumerable<UserDeviceRelationshipObservation> MatchObservations(
        string directorySid, DeviceUserEvidence evidence, DateTimeOffset capturedAtUtc)
    {
        if (evidence.InteractiveUserState == UserEvidenceSourceState.Available
            && string.Equals(evidence.InteractiveUser?.Sid, directorySid, StringComparison.OrdinalIgnoreCase))
        {
            yield return new(UserDeviceRelationshipType.LastInteractiveUser, EvidenceSource, capturedAtUtc,
                UserDeviceRelationshipConfidence.High,
                "The directory SID was the interactive domain user when Inventory captured this device.", null);
        }
        if (evidence.LocalProfilesState != UserEvidenceSourceState.Available) { yield break; }
        foreach (LocalUserProfileEvidence profile in evidence.LocalProfiles ?? [])
        {
            if (!string.Equals(profile.Sid, directorySid, StringComparison.OrdinalIgnoreCase)) { continue; }
            yield return new(UserDeviceRelationshipType.ProfilePresent, EvidenceSource, capturedAtUtc,
                UserDeviceRelationshipConfidence.Medium,
                "Inventory observed a local profile with the directory SID; this does not prove ownership.", profile.LastUseAtUtc);
        }
    }
}
