namespace Wec.Modules.EmployeeLifecycle.Application;

public sealed record ItHygieneOverview(
    long SnapshotRevision,
    DateTimeOffset AssessedAtUtc,
    string? DomainName,
    EnvironmentSourceStates Sources,
    HygieneSummary Summary,
    IReadOnlyList<string> KnownHosts);

public sealed record HygieneDevicePage(
    IReadOnlyList<HygieneDevice> Items,
    int Total,
    int Page,
    int PageSize);

public sealed record GetItHygieneOverviewRequest(
    DirectoryInventoryConnection? ActiveDirectory = null,
    KasperskyInventoryConnection? Kaspersky = null,
    bool Force = false,
    string? OperationId = null);

public sealed record ListHygieneDevicesRequest(
    DirectoryInventoryConnection? ActiveDirectory = null,
    KasperskyInventoryConnection? Kaspersky = null,
    string? Search = null,
    string? Filter = null,
    int Page = 1,
    int PageSize = 50,
    string? SortColumn = null,
    string? SortDirection = null,
    string? OperationId = null);

internal static class ItHygienePaging
{
    public static ItHygieneOverview Overview(ItHygieneResult result) => new(
        result.SnapshotRevision,
        result.AssessedAtUtc,
        result.DomainName,
        result.Sources,
        result.Summary,
        result.Devices.Select(device => device.ComputerName).ToList());

    public static HygieneDevicePage Page(ItHygieneResult result, ListHygieneDevicesRequest request)
    {
        IEnumerable<HygieneDevice> query = result.Devices;
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            string search = request.Search.Trim();
            query = query.Where(device => SearchValues(device).Any(value =>
                value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true));
        }

        query = ApplyFilter(query, request.Filter, result.Sources);
        query = ApplySort(query, request.SortColumn, request.SortDirection);

        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize, 1, 100);
        List<HygieneDevice> all = query.ToList();
        return new HygieneDevicePage(
            all.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            all.Count,
            page,
            pageSize);
    }

    private static IEnumerable<string?> SearchValues(HygieneDevice device)
    {
        yield return device.ComputerName;
        yield return device.HostName;
        yield return device.ActiveDirectory.OperatingSystem;
        yield return device.ActiveDirectory.OrganizationalUnit;
        yield return device.Kaspersky.AdministrationGroup;
        yield return device.Opsi.DepotId;
        yield return device.Nessus.IpAddress;
        foreach (string source in device.Nessus.ScanSources)
        {
            yield return source;
        }
        foreach (HygieneFinding finding in device.Assessment.Findings)
        {
            yield return FindingLabel(finding.Code);
            yield return finding.Message;
        }
    }

    private static IEnumerable<HygieneDevice> ApplyFilter(
        IEnumerable<HygieneDevice> devices,
        string? filter,
        EnvironmentSourceStates sources)
    {
        string normalized = filter?.ToUpperInvariant() ?? "ALL";
        return normalized is "" or "ALL"
            ? devices
            : devices.Where(device => MatchesFilter(device, normalized, sources));
    }

    internal static bool MatchesFilter(
        HygieneDevice device,
        string filter,
        EnvironmentSourceStates? sources = null) => filter switch
    {
        "HEALTHY" => device.Assessment.Status == HygieneStatus.Healthy,
        "PROBLEMS" => device.Assessment.Status is HygieneStatus.Warning or HygieneStatus.CleanupCandidate or HygieneStatus.Critical,
        "INCOMPLETE" => sources is null
            ? device.Assessment.Status == HygieneStatus.Incomplete
            : !HygieneAssessmentPolicy.SourcesComplete(sources, requireNessus: true),
        "STALE" => Has(device, HygieneFindingCode.StaleAd, HygieneFindingCode.StaleKaspersky, HygieneFindingCode.StaleOpsi, HygieneFindingCode.StaleNessus),
        "STALE_AD" => Has(device, HygieneFindingCode.StaleAd),
        "STALE_KASPERSKY" => Has(device, HygieneFindingCode.StaleKaspersky),
        "STALE_OPSI" => Has(device, HygieneFindingCode.StaleOpsi),
        "MISSING_AD" => !device.ActiveDirectory.Exists,
        "DISABLED_AD" => device.ActiveDirectory.Enabled == false,
        "OUTDATED" => Has(device, HygieneFindingCode.OutdatedAgent, HygieneFindingCode.OutdatedKes),
        "NESSUS_CRITICAL" => Has(device, HygieneFindingCode.NessusCriticalVulnerabilities),
        "NESSUS_HIGH" => Has(device, HygieneFindingCode.NessusHighVulnerabilities),
        "MISSING_KASPERSKY" => Has(device, HygieneFindingCode.MissingKaspersky),
        "ORPHAN_KASPERSKY" => Has(device, HygieneFindingCode.OrphanKaspersky),
        "MISSING_OPSI" => Has(device, HygieneFindingCode.MissingOpsi),
        "ORPHAN_OPSI" => Has(device, HygieneFindingCode.OrphanOpsi),
        "MISSING_NESSUS" => Has(device, HygieneFindingCode.MissingNessus),
        "STALE_NESSUS" => Has(device, HygieneFindingCode.StaleNessus),
        _ => false,
    };

    private static IOrderedEnumerable<HygieneDevice> ApplySort(IEnumerable<HygieneDevice> devices, string? column, string? direction)
    {
        bool descending = string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase);
        return column?.ToUpperInvariant() switch
        {
            "OVERALL" => descending
                ? devices.OrderByDescending(device => StatusRank(device.Assessment.Status)).ThenBy(device => device.ComputerName, StringComparer.OrdinalIgnoreCase)
                : devices.OrderBy(device => StatusRank(device.Assessment.Status)).ThenBy(device => device.ComputerName, StringComparer.OrdinalIgnoreCase),
            _ => descending
                ? devices.OrderByDescending(device => device.ComputerName, StringComparer.OrdinalIgnoreCase)
                : devices.OrderBy(device => device.ComputerName, StringComparer.OrdinalIgnoreCase),
        };
    }

    private static int StatusRank(HygieneStatus status) => status switch
    {
        HygieneStatus.Critical => 4,
        HygieneStatus.CleanupCandidate => 3,
        HygieneStatus.Warning => 2,
        HygieneStatus.Incomplete => 1,
        _ => 0,
    };

    private static bool Has(HygieneDevice device, params HygieneFindingCode[] codes) =>
        device.Assessment.Findings.Any(finding => codes.Contains(finding.Code));

    private static string FindingLabel(HygieneFindingCode code) => code switch
    {
        HygieneFindingCode.MissingKaspersky => "Missing Kaspersky",
        HygieneFindingCode.OrphanKaspersky => "Orphan Kaspersky",
        HygieneFindingCode.StaleAd => "Stale AD",
        HygieneFindingCode.StaleKaspersky => "Stale Kaspersky",
        HygieneFindingCode.OutdatedAgent => "Outdated Agent",
        HygieneFindingCode.OutdatedKes => "Outdated KES",
        HygieneFindingCode.MissingOpsi => "Missing opsi",
        HygieneFindingCode.OrphanOpsi => "Orphan opsi",
        HygieneFindingCode.StaleOpsi => "Stale opsi",
        HygieneFindingCode.MissingNessus => "Missing Nessus",
        HygieneFindingCode.StaleNessus => "Stale Nessus",
        HygieneFindingCode.NessusCriticalVulnerabilities => "Critical vulnerabilities",
        HygieneFindingCode.NessusHighVulnerabilities => "High vulnerabilities",
        HygieneFindingCode.MissingKasperskyAgent => "Missing Kaspersky Network Agent",
        HygieneFindingCode.MissingKes => "Missing KES",
        _ => code.ToString(),
    };
}
