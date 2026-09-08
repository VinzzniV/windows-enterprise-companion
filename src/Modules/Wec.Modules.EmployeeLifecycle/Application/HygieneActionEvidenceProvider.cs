using Wec.Core.Contracts;
using Wec.Core.Results;

namespace Wec.Modules.EmployeeLifecycle.Application;

internal sealed class HygieneActionEvidenceProvider(
    ItHygieneService service,
    ItHygieneSnapshotCache cache) : IHygieneActionEvidenceProvider
{
    public async Task<Result<HygieneActionEvidenceSnapshot>> LoadAsync(
        HygieneActionEvidenceQuery query,
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
            ? Result.Failure<HygieneActionEvidenceSnapshot>(loaded.Error!)
            : Result.Success(Project(loaded.Value));
    }

    internal static HygieneActionEvidenceSnapshot Project(ItHygieneResult result)
    {
        IReadOnlyList<ActionEvidenceSourceState> sources =
        [
            Source("Active Directory", result.Sources.ActiveDirectory),
            Source("Kaspersky", result.Sources.Kaspersky),
            Source("opsi", result.Sources.Opsi),
            Source("Nessus", result.Sources.Nessus),
        ];
        List<HygieneActionEvidence> findings = result.Devices
            .SelectMany(device => device.Assessment.Findings.Select(finding => Project(device, finding, result)))
            .OrderBy(finding => finding.SubjectKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(finding => finding.FindingCode, StringComparer.Ordinal)
            .ToList();
        IReadOnlyList<HygieneActionSubject> subjects = result.Devices
            .Select(device => new HygieneActionSubject(device.ComputerName, device.HostName))
            .OrderBy(subject => subject.SubjectKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new HygieneActionEvidenceSnapshot(result.SnapshotRevision, result.AssessedAtUtc, sources, subjects, findings);
    }

    private static HygieneActionEvidence Project(
        HygieneDevice device,
        HygieneFinding finding,
        ItHygieneResult result)
    {
        (string source, DateTimeOffset? evidenceAtUtc, InventorySourceState state) = finding.Code switch
        {
            HygieneFindingCode.StaleAd => ("Active Directory", device.ActiveDirectory.LastLogonDate, result.Sources.ActiveDirectory),
            HygieneFindingCode.MissingOpsi or HygieneFindingCode.OrphanOpsi or HygieneFindingCode.StaleOpsi =>
                ("opsi", device.Opsi.LastSeen, result.Sources.Opsi),
            HygieneFindingCode.MissingNessus or HygieneFindingCode.StaleNessus
                or HygieneFindingCode.NessusCriticalVulnerabilities or HygieneFindingCode.NessusHighVulnerabilities =>
                ("Nessus", device.Nessus.LastCompletedScanUtc, result.Sources.Nessus),
            _ => ("Kaspersky", device.Kaspersky.LastSeen, result.Sources.Kaspersky),
        };
        return new HygieneActionEvidence(
            device.ComputerName,
            device.HostName,
            finding.Code.ToString(),
            finding.Severity.ToString(),
            finding.Message,
            source,
            evidenceAtUtc,
            Availability(state.Availability),
            CoverageExplanation(source, state));
    }

    private static ActionEvidenceSourceState Source(string source, InventorySourceState state) =>
        new(source, Availability(state.Availability), CoverageExplanation(source, state));

    private static ActionEvidenceAvailability Availability(InventorySourceAvailability availability) => availability switch
    {
        InventorySourceAvailability.Available => ActionEvidenceAvailability.Available,
        InventorySourceAvailability.Partial => ActionEvidenceAvailability.Partial,
        InventorySourceAvailability.NotConnected => ActionEvidenceAvailability.NotConnected,
        InventorySourceAvailability.Truncated => ActionEvidenceAvailability.Truncated,
        _ => ActionEvidenceAvailability.Unavailable,
    };

    private static string CoverageExplanation(string source, InventorySourceState state) =>
        state.Availability == InventorySourceAvailability.Available
            ? $"{source} inventory was available for this assessment."
            : string.IsNullOrWhiteSpace(state.Error)
                ? $"{source} inventory coverage is {state.Availability}."
                : state.Error;
}
