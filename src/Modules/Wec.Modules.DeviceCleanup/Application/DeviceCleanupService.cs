using Microsoft.Extensions.Options;
using Wec.Core.Contracts;
using Wec.Core.Results;

namespace Wec.Modules.DeviceCleanup.Application;

internal sealed class DeviceCleanupService(
    IDeviceCleanupEvidenceProvider sourceEvidenceProvider,
    IInventoryClientSnapshotProvider inventorySnapshots,
    IDeviceCleanupInventoryEvidenceProvider inventoryEvidenceProvider,
    IOptions<DeviceCleanupOptions> options)
{
    private static readonly HashSet<string> CleanupFindingCodes = new(StringComparer.Ordinal)
    {
        "StaleAd",
        "StaleKaspersky",
        "StaleOpsi",
        "StaleNessus",
        "OrphanKaspersky",
        "OrphanOpsi",
    };

    public async Task<Result<DeviceCleanupPage>> GetPageAsync(
        ListDeviceCleanupCandidatesRequest request,
        CancellationToken cancellationToken)
    {
        Error? validationError = Validate(request);
        if (validationError is not null)
        {
            return Result.Failure<DeviceCleanupPage>(validationError);
        }

        Task<Result<DeviceCleanupEvidenceSnapshot>> sourceTask = sourceEvidenceProvider.LoadAsync(
            new DeviceCleanupEvidenceQuery(
                request.ActiveDirectory,
                request.Kaspersky,
                request.OperationId,
                request.Force),
            cancellationToken);
        Task<IReadOnlyList<InventoryClientSnapshotHost>> inventoryTask =
            inventorySnapshots.ListHostsAsync(cancellationToken);
        await Task.WhenAll(sourceTask, inventoryTask);

        Result<DeviceCleanupEvidenceSnapshot> sourceResult = await sourceTask;
        if (sourceResult.IsFailure)
        {
            return Result.Failure<DeviceCleanupPage>(sourceResult.Error!);
        }

        DeviceCleanupEvidenceSnapshot sourceSnapshot = sourceResult.Value;
        IReadOnlyList<InventoryClientSnapshotHost> inventory = await inventoryTask;
        Dictionary<string, DeviceCleanupSubjectEvidence> subjects = sourceSnapshot.Subjects
            .GroupBy(subject => Key(subject.SubjectKey), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, InventoryClientSnapshotHost> storedInventory = inventory
            .GroupBy(item => Key(item.Host), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.CapturedAtUtc).First(),
                StringComparer.OrdinalIgnoreCase);
        List<string> subjectKeys = subjects.Keys
            .Concat(storedInventory.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .Take(options.Value.MaximumSubjects)
            .ToList();
        bool subjectsTruncated = subjects.Keys
            .Concat(storedInventory.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Skip(options.Value.MaximumSubjects)
            .Any();

        List<DeviceCleanupCandidate> all = subjectKeys
            .Select(key => Classify(
                subjects.GetValueOrDefault(key),
                storedInventory.GetValueOrDefault(key),
                sourceSnapshot,
                options.Value))
            .ToList();
        IEnumerable<DeviceCleanupCandidate> filtered = request.IncludeWithoutSignals
            ? all
            : all.Where(candidate => candidate.Classification is
                DeviceCleanupClassification.PotentialCleanup or DeviceCleanupClassification.Review);
        string search = request.Search?.Trim() ?? string.Empty;
        if (search.Length > 0)
        {
            filtered = filtered.Where(candidate =>
                $"{candidate.Host} {candidate.SubjectKey} {candidate.ClassificationExplanation}"
                    .Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        List<DeviceCleanupCandidate> matches = filtered
            .OrderBy(candidate => candidate.Classification)
            .ThenBy(candidate => candidate.Host, StringComparer.OrdinalIgnoreCase)
            .ToList();
        long offset = (long)(request.Page - 1) * request.PageSize;
        IReadOnlyList<DeviceCleanupCandidate> page = offset >= matches.Count
            ? []
            : [.. matches.Skip((int)offset).Take(request.PageSize)];

        DeviceCleanupAssessment? selected = null;
        if (!string.IsNullOrWhiteSpace(request.SelectedHost))
        {
            string selectedKey = Key(request.SelectedHost);
            DeviceCleanupCandidate? candidate = all.FirstOrDefault(item =>
                string.Equals(item.SubjectKey, selectedKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Host, request.SelectedHost.Trim(), StringComparison.OrdinalIgnoreCase));
            if (candidate is null)
            {
                return Result.Failure<DeviceCleanupPage>(Error.NotFound(
                    $"No cleanup evidence was found for '{request.SelectedHost.Trim()}'."));
            }

            DeviceCleanupInventoryEvidence? selectedInventory =
                await inventoryEvidenceProvider.GetLatestAsync(candidate.SubjectKey, cancellationToken);
            selected = ComposeAssessment(
                candidate,
                subjects.GetValueOrDefault(candidate.SubjectKey),
                selectedInventory,
                sourceSnapshot.Sources);
        }

        IReadOnlyList<ActionEvidenceSourceState> sources =
        [
            .. sourceSnapshot.Sources,
            new ActionEvidenceSourceState(
                "WEC Inventory",
                ActionEvidenceAvailability.Available,
                inventory.Count == 0
                    ? "No stored Inventory snapshots are available."
                    : $"{inventory.Count} latest stored Inventory snapshot(s) were evaluated."),
        ];
        return Result.Success(new DeviceCleanupPage(
            page,
            matches.Count,
            request.Page,
            request.PageSize,
            sourceSnapshot.AssessedAtUtc,
            sources,
            selected,
            subjectsTruncated));
    }

    private Error? Validate(ListDeviceCleanupCandidatesRequest request)
    {
        if (request.Page < 1 || request.PageSize < 1 || request.PageSize > options.Value.MaximumPageSize)
        {
            return new Error(
                ErrorCode.InvalidRequest,
                $"Page must be positive and PageSize must be between 1 and {options.Value.MaximumPageSize}.");
        }

        if (request.Search?.Length > 200 || request.SelectedHost?.Length > 255)
        {
            return new Error(ErrorCode.InvalidRequest, "Search or selected host exceeds the allowed length.");
        }

        return null;
    }

    internal static DeviceCleanupCandidate Classify(
        DeviceCleanupSubjectEvidence? subject,
        InventoryClientSnapshotHost? inventory,
        DeviceCleanupEvidenceSnapshot snapshot,
        DeviceCleanupOptions options)
    {
        List<DeviceCleanupFindingEvidence> relevantFindings = subject?.Findings
            .Where(finding => CleanupFindingCodes.Contains(finding.Code))
            .ToList() ?? [];
        bool criticalStale = relevantFindings.Any(finding =>
            finding.Code.StartsWith("Stale", StringComparison.Ordinal)
            && string.Equals(finding.Severity, "Critical", StringComparison.Ordinal));
        int? inventoryAgeDays = inventory is null
            ? null
            : AgeDays(inventory.CapturedAtUtc, snapshot.AssessedAtUtc);
        bool disabledAd = subject?.ActiveDirectory is { Exists: true, Enabled: false };
        bool staleInventory = inventoryAgeDays > options.InventoryStaleWarningDays;
        bool incompleteSources = snapshot.Sources.Any(source =>
            source.Availability != ActionEvidenceAvailability.Available);

        DeviceCleanupClassification classification;
        string explanation;
        if (criticalStale)
        {
            classification = DeviceCleanupClassification.PotentialCleanup;
            explanation = "At least one source activity timestamp exceeds its existing cleanup threshold; manual review is still required.";
        }
        else if (disabledAd || relevantFindings.Count > 0 || staleInventory)
        {
            classification = DeviceCleanupClassification.Review;
            var reasons = new List<string>();
            if (disabledAd)
            {
                reasons.Add("the AD computer is disabled");
            }

            if (relevantFindings.Count > 0)
            {
                reasons.Add($"{relevantFindings.Count} stale or orphan source signal(s) exist");
            }

            if (staleInventory)
            {
                reasons.Add($"the stored Inventory snapshot is older than {options.InventoryStaleWarningDays} days");
            }

            explanation = $"Review required because {string.Join(", ", reasons)}.";
        }
        else if (incompleteSources)
        {
            classification = DeviceCleanupClassification.InsufficientEvidence;
            explanation = "No cleanup signal is proven, but one or more management sources were not fully evaluated.";
        }
        else
        {
            classification = DeviceCleanupClassification.NoCleanupSignal;
            explanation = "No stale, orphan or disabled-device signal was found in the evaluated sources.";
        }

        string host = subject?.Host ?? inventory?.Host ?? subject?.SubjectKey ?? string.Empty;
        return new DeviceCleanupCandidate(
            Key(subject?.SubjectKey ?? inventory?.Host ?? string.Empty),
            host,
            classification,
            explanation,
            subject?.ActiveDirectory.Enabled,
            subject?.ActiveDirectory.LastLogonAtUtc,
            subject?.Kaspersky.LastSeenAtUtc,
            subject?.Opsi.LastSeenAtUtc,
            subject?.Nessus.LastCompletedScanAtUtc,
            inventory?.CapturedAtUtc,
            relevantFindings.Count);
    }

    private static DeviceCleanupAssessment ComposeAssessment(
        DeviceCleanupCandidate candidate,
        DeviceCleanupSubjectEvidence? subject,
        DeviceCleanupInventoryEvidence? inventory,
        IReadOnlyList<ActionEvidenceSourceState> sourceStates)
    {
        var states = sourceStates.ToDictionary(
            source => source.Source,
            StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<DeviceCleanupSourceFact> facts =
        [
            Fact(
                "Active Directory",
                states.GetValueOrDefault("Active Directory"),
                subject?.ActiveDirectory.Exists,
                subject?.ActiveDirectory.Exists is true
                    ? subject.ActiveDirectory.Enabled switch { true => "Enabled", false => "Disabled", _ => "Present" }
                    : "Not found",
                subject?.ActiveDirectory.LastLogonAtUtc),
            Fact("Kaspersky", states.GetValueOrDefault("Kaspersky"), subject?.Kaspersky.Exists,
                subject?.Kaspersky.Exists is true ? "Registered" : "Not found", subject?.Kaspersky.LastSeenAtUtc),
            Fact("opsi", states.GetValueOrDefault("opsi"), subject?.Opsi.Exists,
                subject?.Opsi.Exists is true ? "Registered" : "Not found", subject?.Opsi.LastSeenAtUtc),
            Fact("Nessus", states.GetValueOrDefault("Nessus"), subject?.Nessus.Exists,
                subject?.Nessus.Exists is true ? "Observed" : "Not found", subject?.Nessus.LastCompletedScanAtUtc),
            new DeviceCleanupSourceFact(
                "WEC Inventory",
                ActionEvidenceAvailability.Available,
                inventory is not null,
                inventory is null ? "Not scanned" : "Stored snapshot",
                inventory?.CapturedAtUtc,
                inventory is null
                    ? "No stored Inventory snapshot was found; no scan was started."
                    : "The latest stored Inventory snapshot was read without starting a scan."),
        ];
        return new DeviceCleanupAssessment(
            candidate,
            facts,
            subject?.Findings ?? [],
            inventory?.UserEvidenceAvailability ?? DeviceCleanupUserEvidenceAvailability.NotCaptured,
            inventory?.UserEvidenceExplanation ?? "No stored Inventory user/device evidence is available.",
            inventory?.UserObservations ?? []);
    }

    private static DeviceCleanupSourceFact Fact(
        string name,
        ActionEvidenceSourceState? sourceState,
        bool? exists,
        string state,
        DateTimeOffset? observedAtUtc)
    {
        ActionEvidenceAvailability coverage = sourceState?.Availability
            ?? ActionEvidenceAvailability.Unavailable;
        bool sourceAvailable = coverage == ActionEvidenceAvailability.Available;
        return new DeviceCleanupSourceFact(
            name,
            coverage,
            sourceAvailable ? exists : null,
            sourceAvailable ? state : "Not evaluated",
            sourceAvailable ? observedAtUtc : null,
            sourceAvailable
                ? exists is true
                    ? $"{name} evidence was available for this subject."
                    : $"The available {name} inventory contained no matching subject."
                : sourceState?.Explanation ?? $"{name} evidence is unavailable.");
    }

    private static int AgeDays(DateTimeOffset timestamp, DateTimeOffset assessedAtUtc) =>
        (int)Math.Floor(Math.Max(0, (assessedAtUtc - timestamp).TotalDays));

    private static string Key(string value) => value.Trim().ToUpperInvariant();
}
