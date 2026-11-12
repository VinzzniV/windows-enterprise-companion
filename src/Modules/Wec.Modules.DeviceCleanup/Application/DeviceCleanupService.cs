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

        Result<CandidateSet> candidateSetResult = await LoadCandidatesAsync(
            request.ActiveDirectory,
            request.Kaspersky,
            request.OperationId,
            request.Force,
            request.Search,
            request.IncludeWithoutSignals,
            cancellationToken);
        if (candidateSetResult.IsFailure)
        {
            return Result.Failure<DeviceCleanupPage>(candidateSetResult.Error!);
        }

        CandidateSet candidateSet = candidateSetResult.Value;
        long offset = (long)(request.Page - 1) * request.PageSize;
        IReadOnlyList<DeviceCleanupCandidate> page = offset >= candidateSet.Matches.Count
            ? []
            : [.. candidateSet.Matches.Skip((int)offset).Take(request.PageSize)];

        DeviceCleanupAssessment? selected = null;
        if (!string.IsNullOrWhiteSpace(request.SelectedHost))
        {
            string selectedKey = Key(request.SelectedHost);
            DeviceCleanupCandidate[] exact = candidateSet.All.Where(item => string.Equals(item.SubjectKey, selectedKey, StringComparison.OrdinalIgnoreCase)).ToArray();
            DeviceCleanupCandidate[] matches = exact.Length > 0 ? exact : candidateSet.All.Where(item =>
                string.Equals(item.Host, request.SelectedHost.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length > 1)
            {
                return Result.Failure<DeviceCleanupPage>(new(ErrorCode.InvalidRequest, "Multiple source observations use this address. Select an individual cleanup evidence row."));
            }
            DeviceCleanupCandidate? candidate = matches.SingleOrDefault();
            if (candidate is null)
            {
                return Result.Failure<DeviceCleanupPage>(Error.NotFound(
                    $"No cleanup evidence was found for '{request.SelectedHost.Trim()}'."));
            }

            DeviceCleanupInventoryEvidence? selectedInventory =
                candidate.CanTargetWindows ? await inventoryEvidenceProvider.GetLatestAsync(candidate.Host, cancellationToken) : null;
            selected = ComposeAssessment(
                candidate,
                candidateSet.Subjects.GetValueOrDefault(candidate.SubjectKey),
                selectedInventory,
                candidateSet.SourceSnapshot.Sources);
        }

        return Result.Success(new DeviceCleanupPage(
            page,
            candidateSet.Matches.Count,
            request.Page,
            request.PageSize,
            candidateSet.SourceSnapshot.AssessedAtUtc,
            candidateSet.Sources,
            selected,
            candidateSet.SubjectsTruncated));
    }

    public async Task<Result<DeviceCleanupExportSnapshot>> GetExportSnapshotAsync(
        DeviceCleanupExportQuery request,
        CancellationToken cancellationToken)
    {
        if (request.Search?.Length > 200)
        {
            return Result.Failure<DeviceCleanupExportSnapshot>(
                new Error(ErrorCode.InvalidRequest, "Search exceeds the allowed length."));
        }

        Result<CandidateSet> candidateSetResult = await LoadCandidatesAsync(
            request.ActiveDirectory,
            request.Kaspersky,
            request.OperationId,
            force: false,
            request.Search,
            request.IncludeWithoutSignals,
            cancellationToken);
        if (candidateSetResult.IsFailure)
        {
            return Result.Failure<DeviceCleanupExportSnapshot>(candidateSetResult.Error!);
        }

        CandidateSet candidateSet = candidateSetResult.Value;
        return Result.Success(new DeviceCleanupExportSnapshot(
            candidateSet.Matches,
            candidateSet.SourceSnapshot.AssessedAtUtc,
            candidateSet.Sources,
            candidateSet.SubjectsTruncated));
    }

    private async Task<Result<CandidateSet>> LoadCandidatesAsync(
        HygieneActionDirectoryConnection? activeDirectory,
        HygieneActionKasperskyConnection? kaspersky,
        string? operationId,
        bool force,
        string? search,
        bool includeWithoutSignals,
        CancellationToken cancellationToken)
    {
        Task<Result<DeviceCleanupEvidenceSnapshot>> sourceTask = sourceEvidenceProvider.LoadAsync(
            new DeviceCleanupEvidenceQuery(activeDirectory, kaspersky, operationId, force),
            cancellationToken);
        Task<IReadOnlyList<InventoryClientSnapshotHost>> inventoryTask =
            inventorySnapshots.ListHostsAsync(cancellationToken);
        await Task.WhenAll(sourceTask, inventoryTask);

        Result<DeviceCleanupEvidenceSnapshot> sourceResult = await sourceTask;
        if (sourceResult.IsFailure)
        {
            return Result.Failure<CandidateSet>(sourceResult.Error!);
        }

        DeviceCleanupEvidenceSnapshot sourceSnapshot = sourceResult.Value;
        IReadOnlyList<InventoryClientSnapshotHost> inventory = await inventoryTask;
        Dictionary<string, DeviceCleanupSubjectEvidence> subjects = new(StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, DeviceCleanupSubjectEvidence> group in sourceSnapshot.Subjects.GroupBy(subject => Key(subject.SubjectKey), StringComparer.OrdinalIgnoreCase))
        {
            DeviceCleanupSubjectEvidence[] observations = group.ToArray();
            for (int index = 0; index < observations.Length; ++index)
            {
                DeviceCleanupSubjectEvidence observation = observations[index];
                string key = observations.Length == 1 ? group.Key : $"{group.Key}:observation:{index}";
                subjects.Add(key, observation with { SubjectKey = key, CanTargetWindows = observation.CanTargetWindows && observations.Length == 1 });
            }
        }
        Dictionary<string, InventoryClientSnapshotHost> storedInventory = inventory
            .GroupBy(item => Key(item.Host), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.CapturedAtUtc).First(),
                StringComparer.OrdinalIgnoreCase);
        var matchedInventory = subjects.Where(pair => pair.Value.CanTargetWindows)
            .Select(pair => Key(pair.Value.Host)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> distinctKeys = subjects.Keys
            .Concat(storedInventory.Keys.Where(key => !matchedInventory.Contains(key)))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        List<string> subjectKeys = distinctKeys
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .Take(options.Value.MaximumSubjects)
            .ToList();
        bool subjectsTruncated = distinctKeys.Skip(options.Value.MaximumSubjects).Any();

        List<DeviceCleanupCandidate> all = subjectKeys
            .Select(key => Classify(
                subjects.GetValueOrDefault(key),
                subjects.TryGetValue(key, out DeviceCleanupSubjectEvidence? subject)
                    ? subject.CanTargetWindows ? storedInventory.GetValueOrDefault(Key(subject.Host)) : null : storedInventory.GetValueOrDefault(key),
                sourceSnapshot,
                options.Value))
            .ToList();
        IEnumerable<DeviceCleanupCandidate> filtered = includeWithoutSignals
            ? all
            : all.Where(candidate => candidate.Classification is
                DeviceCleanupClassification.PotentialCleanup or DeviceCleanupClassification.Review);
        string trimmedSearch = search?.Trim() ?? string.Empty;
        if (trimmedSearch.Length > 0)
        {
            filtered = filtered.Where(candidate =>
                $"{candidate.Host} {candidate.SubjectKey} {candidate.Description} {candidate.ClassificationExplanation}"
                    .Contains(trimmedSearch, StringComparison.OrdinalIgnoreCase));
        }

        List<DeviceCleanupCandidate> matches = filtered
            .OrderBy(candidate => candidate.Classification)
            .ThenBy(candidate => candidate.Host, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
        return Result.Success(new CandidateSet(
            all,
            matches,
            subjects,
            sourceSnapshot,
            sources,
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
        if (subject?.CanTargetWindows == false)
        {
            classification = DeviceCleanupClassification.Review;
            explanation = "The source observation has no unambiguous Windows target. Review its identity before drawing a cleanup conclusion; connectivity checks are unavailable.";
        }
        else if (criticalStale)
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

        if (subject?.IdentityExplanation is { } identityExplanation) { explanation += " " + identityExplanation; }
        string host = subject?.Host ?? inventory?.Host ?? subject?.SubjectKey ?? string.Empty;
        string? description = FirstDescription(subject?.ActiveDirectory.Description, subject?.Opsi.Description);
        string? descriptionSource = description is null
            ? null
            : !string.IsNullOrWhiteSpace(subject?.ActiveDirectory.Description)
                ? "Active Directory"
                : "opsi";
        return new DeviceCleanupCandidate(
            Key(subject?.SubjectKey ?? inventory?.Host ?? string.Empty),
            host,
            description,
            descriptionSource,
            classification,
            explanation,
            Presence(subject?.ActiveDirectory.Exists, "Active Directory"),
            subject?.ActiveDirectory.Enabled,
            subject?.ActiveDirectory.LastLogonAtUtc,
            Presence(subject?.Kaspersky.Exists, "Kaspersky"),
            subject?.Kaspersky.LastSeenAtUtc,
            Presence(subject?.Opsi.Exists, "opsi"),
            subject?.Opsi.LastSeenAtUtc,
            Presence(subject?.Nessus.Exists, "Nessus"),
            subject?.Nessus.LastCompletedScanAtUtc,
            inventory is not null,
            inventory?.CapturedAtUtc,
            relevantFindings.Count) { CanTargetWindows = subject?.CanTargetWindows ?? true };

        bool? Presence(bool? exists, string source) => subject?.CanTargetWindows == false && exists != true
            ? null : ExistsWhenAvailable(exists, snapshot, source);
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
        if (subject?.CanTargetWindows == false)
        {
            foreach (string source in states.Keys.ToArray())
            {
                bool exists = source switch
                {
                    "Active Directory" => subject.ActiveDirectory.Exists, "Kaspersky" => subject.Kaspersky.Exists,
                    "opsi" => subject.Opsi.Exists, "Nessus" => subject.Nessus.Exists, _ => false,
                };
                if (!exists) { states[source] = states[source] with { Availability = ActionEvidenceAvailability.Partial,
                    Explanation = "Unresolved source identity; absence in another source is not established." }; }
            }
        }
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

    private static string? FirstDescription(params string?[] values) => values
        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
        ?.Trim();

    private static bool? ExistsWhenAvailable(
        bool? exists,
        DeviceCleanupEvidenceSnapshot snapshot,
        string source)
    {
        if (exists is true)
        {
            return true;
        }

        return snapshot.Sources.FirstOrDefault(item =>
            string.Equals(item.Source, source, StringComparison.OrdinalIgnoreCase))?.Availability
            == ActionEvidenceAvailability.Available
                ? false
                : null;
    }

    private static string Key(string value) => value.Trim().ToUpperInvariant();

    private sealed record CandidateSet(
        IReadOnlyList<DeviceCleanupCandidate> All,
        IReadOnlyList<DeviceCleanupCandidate> Matches,
        IReadOnlyDictionary<string, DeviceCleanupSubjectEvidence> Subjects,
        DeviceCleanupEvidenceSnapshot SourceSnapshot,
        IReadOnlyList<ActionEvidenceSourceState> Sources,
        bool SubjectsTruncated);
}
