using Microsoft.Extensions.Options;
using Wec.Core.Contracts;
using Wec.Core.Results;

namespace Wec.Modules.ActionCenter.Application;

internal sealed class ActionCenterService(
    IHygieneActionEvidenceProvider hygieneProvider,
    IInventoryClientSnapshotProvider inventoryProvider,
    ISecurityActionEvidenceProvider securityProvider,
    IOptions<ActionCenterOptions> options)
{
    public async Task<Result<ActionCenterPage>> GetPageAsync(
        ListActionCenterItemsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Page < 1 || request.PageSize < 1 || request.PageSize > options.Value.MaximumPageSize)
        {
            return Result.Failure<ActionCenterPage>(new Error(
                ErrorCode.InvalidRequest,
                $"Page must be positive and PageSize must be between 1 and {options.Value.MaximumPageSize}."));
        }

        Task<Result<HygieneActionEvidenceSnapshot>> hygieneTask = hygieneProvider.LoadAsync(
            new HygieneActionEvidenceQuery(
                request.ActiveDirectory,
                request.Kaspersky,
                request.OperationId,
                request.Force),
            cancellationToken);
        Task<IReadOnlyList<InventoryClientSnapshotHost>> inventoryTask =
            inventoryProvider.ListHostsAsync(cancellationToken);
        Task<SecurityActionEvidenceSnapshot> securityTask = securityProvider.LoadStoredAsync(
            options.Value.MaximumSecurityScans,
            cancellationToken);
        await Task.WhenAll(hygieneTask, inventoryTask, securityTask);

        Result<HygieneActionEvidenceSnapshot> hygieneResult = await hygieneTask;
        if (hygieneResult.IsFailure)
        {
            return Result.Failure<ActionCenterPage>(hygieneResult.Error!);
        }

        HygieneActionEvidenceSnapshot hygiene = hygieneResult.Value;
        IReadOnlyList<InventoryClientSnapshotHost> inventory = await inventoryTask;
        SecurityActionEvidenceSnapshot security = await securityTask;
        List<ActionCenterWorkItem> all =
        [
            .. hygiene.Findings.Select(finding => FromHygiene(finding, hygiene.AssessedAtUtc)),
            .. InventoryItems(inventory, hygiene.AssessedAtUtc, options.Value.InventoryStaleWarningDays),
            .. security.Findings.Select(finding => FromSecurity(finding, hygiene.AssessedAtUtc)),
        ];
        List<ActionCenterWorkItem> bounded = all
            .OrderBy(item => SeverityRank(item.Severity))
            .ThenBy(item => item.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SubjectKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ProblemCode, StringComparer.Ordinal)
            .Take(options.Value.MaximumComputedItems)
            .ToList();
        bool itemsTruncated = all.Count > bounded.Count || security.ScansTruncated;

        IEnumerable<ActionCenterWorkItem> filtered = bounded;
        string search = request.Search?.Trim() ?? string.Empty;
        if (search.Length > 0)
        {
            filtered = filtered.Where(item => SearchText(item).Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (request.Severity is not null)
        {
            filtered = filtered.Where(item => item.Severity == request.Severity);
        }

        string source = request.Source?.Trim() ?? string.Empty;
        if (source.Length > 0)
        {
            filtered = filtered.Where(item => string.Equals(item.Source, source, StringComparison.OrdinalIgnoreCase));
        }

        List<ActionCenterWorkItem> matches = ApplySort(filtered, request.SortField, request.SortDirection).ToList();
        long offset = (long)(request.Page - 1) * request.PageSize;
        List<ActionCenterWorkItem> page = offset >= matches.Count
            ? []
            : matches.Skip((int)offset).Take(request.PageSize).ToList();
        IReadOnlyList<ActionEvidenceSourceState> sources =
        [
            .. hygiene.Sources,
            new("WEC Inventory", ActionEvidenceAvailability.Available,
                inventory.Count == 0 ? "No stored Inventory snapshots are available." : $"{inventory.Count} latest stored Inventory snapshots were evaluated."),
            new("WEC Security", security.ScansTruncated ? ActionEvidenceAvailability.Truncated : ActionEvidenceAvailability.Available,
                security.ScansTruncated
                    ? $"Only {security.EvaluatedScanCount} latest Security scans were evaluated."
                    : $"{security.EvaluatedScanCount} latest stored Security scans were evaluated."),
        ];
        return Result.Success(new ActionCenterPage(
            page,
            matches.Count,
            request.Page,
            request.PageSize,
            Summarize(matches),
            hygiene.SnapshotRevision,
            hygiene.AssessedAtUtc,
            sources,
            itemsTruncated));
    }

    private static ActionCenterWorkItem FromHygiene(
        HygieneActionEvidence finding,
        DateTimeOffset assessedAtUtc)
    {
        ActionCenterSeverity severity = finding.FindingCode == "NessusCriticalVulnerabilities"
            ? ActionCenterSeverity.Critical
            : string.Equals(finding.Severity, "Critical", StringComparison.Ordinal)
                ? ActionCenterSeverity.High
                : ActionCenterSeverity.Warning;
        bool cleanupEvidence = IsCleanupEvidence(finding.FindingCode);
        bool nessus = finding.Source == "Nessus";
        string href = cleanupEvidence
            ? $"/cleanup?host={Uri.EscapeDataString(finding.Host)}"
            : nessus
                ? $"/vulnerabilities?tab=findings&asset={Uri.EscapeDataString(finding.Host)}"
                : $"/clients/{Uri.EscapeDataString(finding.Host)}";
        return new ActionCenterWorkItem(
            $"hygiene:{finding.SubjectKey}:{finding.FindingCode}",
            "Device",
            finding.SubjectKey,
            finding.Host,
            null,
            null,
            finding.FindingCode,
            ProblemLabel(finding.FindingCode),
            finding.Message,
            finding.Source,
            severity,
            finding.EvidenceAtUtc,
            assessedAtUtc,
            AgeDays(finding.EvidenceAtUtc, assessedAtUtc),
            finding.Coverage,
            Reliability(finding.Coverage),
            finding.CoverageExplanation,
            RecommendedAction(finding.FindingCode),
            href);
    }

    private static IEnumerable<ActionCenterWorkItem> InventoryItems(
        IReadOnlyList<InventoryClientSnapshotHost> snapshots,
        DateTimeOffset assessedAtUtc,
        int staleWarningDays) => snapshots
        .Where(snapshot => AgeDays(snapshot.CapturedAtUtc, assessedAtUtc) > staleWarningDays)
        .Select(snapshot => new ActionCenterWorkItem(
            $"inventory:{snapshot.Host}:stale",
            "Device",
            snapshot.Host.ToUpperInvariant(),
            snapshot.Host,
            null,
            null,
            "INVENTORY_STALE",
            "Stored Inventory snapshot is stale",
            $"The latest stored Inventory snapshot is older than the configured {staleWarningDays}-day warning threshold.",
            "WEC Inventory",
            ActionCenterSeverity.Warning,
            snapshot.CapturedAtUtc,
            assessedAtUtc,
            AgeDays(snapshot.CapturedAtUtc, assessedAtUtc),
            ActionEvidenceAvailability.Available,
            "High",
            "The latest persisted Inventory timestamp was read without starting a scan.",
            "Open Device Cleanup to compare this timestamp with the other retained source facts.",
            $"/cleanup?host={Uri.EscapeDataString(snapshot.Host)}"));

    private static ActionCenterWorkItem FromSecurity(
        SecurityActionEvidence finding,
        DateTimeOffset assessedAtUtc) => new(
            $"security:{finding.SubjectKey}:{finding.FindingId}",
            "Device",
            finding.SubjectKey,
            finding.Host,
            null,
            null,
            finding.FindingId,
            finding.Title,
            finding.Description,
            "WEC Security",
            ParseSecuritySeverity(finding.Severity),
            finding.CapturedAtUtc,
            assessedAtUtc,
            AgeDays(finding.CapturedAtUtc, assessedAtUtc),
            finding.Coverage,
            Reliability(finding.Coverage),
            finding.CoverageExplanation,
            string.IsNullOrWhiteSpace(finding.Recommendation)
                ? "Open the stored Security scan and review the finding."
                : finding.Recommendation,
            $"/clients/{Uri.EscapeDataString(finding.Host)}?section=security");

    private static ActionCenterSummary Summarize(List<ActionCenterWorkItem> items) => new(
        items.Count,
        items.Select(item => item.SubjectKey).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
        items.Count(item => item.Severity == ActionCenterSeverity.Critical),
        items.Count(item => item.Severity == ActionCenterSeverity.High),
        items.Count(item => item.Severity == ActionCenterSeverity.Warning),
        items.Count(item => item.Coverage != ActionEvidenceAvailability.Available));

    private static IOrderedEnumerable<ActionCenterWorkItem> ApplySort(
        IEnumerable<ActionCenterWorkItem> items,
        ActionCenterSortField field,
        ActionCenterSortDirection direction)
    {
        Func<ActionCenterWorkItem, object?> key = field switch
        {
            ActionCenterSortField.Device => item => item.Device,
            ActionCenterSortField.Source => item => item.Source,
            ActionCenterSortField.EvidenceAge => item => item.EvidenceAgeDays ?? -1,
            ActionCenterSortField.Problem => item => item.Problem,
            _ => item => SeverityRank(item.Severity),
        };
        IOrderedEnumerable<ActionCenterWorkItem> ordered = direction == ActionCenterSortDirection.Descending
            ? items.OrderByDescending(key, Comparer<object?>.Default)
            : items.OrderBy(key, Comparer<object?>.Default);
        return ordered
            .ThenBy(item => item.Device, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ProblemCode, StringComparer.Ordinal);
    }

    private static string SearchText(ActionCenterWorkItem item) =>
        $"{item.Device} {item.Problem} {item.Explanation} {item.Source} {item.RecommendedAction}";

    private static int? AgeDays(DateTimeOffset? evidenceAtUtc, DateTimeOffset assessedAtUtc) => evidenceAtUtc is null
        ? null
        : (int)Math.Floor(Math.Max(0, (assessedAtUtc - evidenceAtUtc.Value).TotalDays));

    private static int SeverityRank(ActionCenterSeverity severity) => severity switch
    {
        ActionCenterSeverity.Critical => 0,
        ActionCenterSeverity.High => 1,
        ActionCenterSeverity.Warning => 2,
        ActionCenterSeverity.Medium => 3,
        ActionCenterSeverity.Low => 4,
        ActionCenterSeverity.Information => 5,
        _ => 6,
    };

    private static ActionCenterSeverity ParseSecuritySeverity(string severity) => severity switch
    {
        "Critical" => ActionCenterSeverity.Critical,
        "High" => ActionCenterSeverity.High,
        "Medium" => ActionCenterSeverity.Medium,
        "Low" => ActionCenterSeverity.Low,
        "Info" => ActionCenterSeverity.Information,
        _ => ActionCenterSeverity.Unknown,
    };

    private static string Reliability(ActionEvidenceAvailability availability) => availability switch
    {
        ActionEvidenceAvailability.Available => "High",
        ActionEvidenceAvailability.Partial or ActionEvidenceAvailability.Truncated => "Medium",
        _ => "Unknown",
    };

    private static string ProblemLabel(string code) => code switch
    {
        "MissingKaspersky" => "Device is missing from Kaspersky",
        "OrphanKaspersky" => "Kaspersky device has no matching AD computer",
        "StaleAd" => "Active Directory activity is stale",
        "StaleKaspersky" => "Kaspersky activity is stale",
        "OutdatedAgent" => "Kaspersky Network Agent is outdated",
        "OutdatedKes" => "KES is outdated",
        "MissingOpsi" => "Windows client is missing from opsi",
        "OrphanOpsi" => "opsi client has no matching AD computer",
        "StaleOpsi" => "opsi activity is stale",
        "MissingNessus" => "Windows device has no completed Nessus scan",
        "StaleNessus" => "Nessus scan is stale",
        "NessusCriticalVulnerabilities" => "Nessus reports critical vulnerabilities",
        "NessusHighVulnerabilities" => "Nessus reports high vulnerabilities",
        "MissingKasperskyAgent" => "Kaspersky Network Agent version is missing",
        "MissingKes" => "KES version is missing",
        _ => "Fleet posture requires review",
    };

    private static string RecommendedAction(string code) => code switch
    {
        "StaleAd" or "StaleKaspersky" or "StaleOpsi" or "StaleNessus" or "OrphanKaspersky" or "OrphanOpsi" =>
            "Open Device Cleanup and review all retained source facts before making a manual decision.",
        "OutdatedAgent" or "OutdatedKes" => "Open Client 360 and Patch Management to plan the approved package update.",
        "MissingNessus" or "NessusCriticalVulnerabilities" or "NessusHighVulnerabilities" =>
            "Open Vulnerabilities and review the matching stored Nessus evidence.",
        "MissingOpsi" =>
            "Open Client 360 and verify the opsi registration before changing either system.",
        "MissingKaspersky" or "MissingKasperskyAgent" or "MissingKes" =>
            "Open Client 360 and verify the Kaspersky registration and source freshness.",
        _ => "Open Client 360 and review the source evidence.",
    };

    private static bool IsCleanupEvidence(string code) => code is
        "StaleAd" or "StaleKaspersky" or "StaleOpsi" or "StaleNessus" or "OrphanKaspersky" or "OrphanOpsi";
}
