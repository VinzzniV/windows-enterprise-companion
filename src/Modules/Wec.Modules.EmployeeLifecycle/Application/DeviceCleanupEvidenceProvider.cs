using Wec.Core.Contracts;
using Wec.Core.Results;

namespace Wec.Modules.EmployeeLifecycle.Application;

internal sealed class DeviceCleanupEvidenceProvider(
    ItHygieneService service,
    ItHygieneSnapshotCache cache) : IDeviceCleanupEvidenceProvider
{
    public async Task<Result<DeviceCleanupEvidenceSnapshot>> LoadAsync(
        DeviceCleanupEvidenceQuery query,
        CancellationToken cancellationToken)
    {
        var request = new ItHygieneRequest(
            query.ActiveDirectory is null
                ? null
                : new DirectoryInventoryConnection(
                    query.ActiveDirectory.Domain,
                    query.ActiveDirectory.Server,
                    query.ActiveDirectory.UserName,
                    query.ActiveDirectory.UserDomain,
                    query.ActiveDirectory.Password),
            query.Kaspersky is null
                ? null
                : new KasperskyInventoryConnection(
                    query.Kaspersky.Server,
                    query.Kaspersky.Port,
                    query.Kaspersky.UserName,
                    query.Kaspersky.Domain,
                    query.Kaspersky.Password),
            query.OperationId);
        Result<ItHygieneResult> loaded = await cache.GetAsync(
            request, query.Force, service.LoadAsync, cancellationToken);
        return loaded.IsFailure
            ? Result.Failure<DeviceCleanupEvidenceSnapshot>(loaded.Error!)
            : Result.Success(Project(loaded.Value));
    }

    internal static DeviceCleanupEvidenceSnapshot Project(ItHygieneResult result) => new(
        result.AssessedAtUtc,
        [
            Source("Active Directory", result.Sources.ActiveDirectory),
            Source("Kaspersky", result.Sources.Kaspersky),
            Source("opsi", result.Sources.Opsi),
            Source("Nessus", result.Sources.Nessus),
        ],
        [.. result.Devices
            .OrderBy(device => device.ComputerName, StringComparer.OrdinalIgnoreCase)
            .Select(Project)]);

    private static DeviceCleanupSubjectEvidence Project(HygieneDevice device) => new(
        device.ComputerName,
        device.HostName,
        device.Assessment.Status.ToString(),
        new DeviceCleanupAdEvidence(
            device.ActiveDirectory.Exists,
            device.ActiveDirectory.Enabled,
            device.ActiveDirectory.OperatingSystem,
            device.ActiveDirectory.Description,
            device.ActiveDirectory.DistinguishedName,
            device.ActiveDirectory.OrganizationalUnit,
            device.ActiveDirectory.LastLogonDate),
        new DeviceCleanupKasperskyEvidence(
            device.Kaspersky.Exists,
            device.Kaspersky.LastSeen,
            device.Kaspersky.AdministrationGroup),
        new DeviceCleanupOpsiEvidence(
            device.Opsi.Exists,
            device.Opsi.Description,
            device.Opsi.LastSeen,
            device.Opsi.DepotId),
        new DeviceCleanupNessusEvidence(
            device.Nessus.Exists,
            device.Nessus.LastCompletedScanUtc),
        [.. device.Assessment.Findings.Select(finding => new DeviceCleanupFindingEvidence(
            finding.Code.ToString(),
            finding.Severity.ToString(),
            finding.Message))]);

    private static ActionEvidenceSourceState Source(string source, InventorySourceState state) => new(
        source,
        state.Availability switch
        {
            InventorySourceAvailability.Available => ActionEvidenceAvailability.Available,
            InventorySourceAvailability.Partial => ActionEvidenceAvailability.Partial,
            InventorySourceAvailability.NotConnected => ActionEvidenceAvailability.NotConnected,
            InventorySourceAvailability.Truncated => ActionEvidenceAvailability.Truncated,
            _ => ActionEvidenceAvailability.Unavailable,
        },
        state.Availability == InventorySourceAvailability.Available
            ? $"{source} inventory was available for this assessment."
            : string.IsNullOrWhiteSpace(state.Error)
                ? $"{source} inventory coverage is {state.Availability}."
                : state.Error);
}
