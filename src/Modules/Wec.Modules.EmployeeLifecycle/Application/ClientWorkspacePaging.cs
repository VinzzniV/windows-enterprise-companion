using Wec.Core.Contracts;

namespace Wec.Modules.EmployeeLifecycle.Application;

public sealed record ClientWorkspaceListItem(
    string Host,
    string Key,
    string Name,
    string? Os,
    string? Description,
    bool Enabled,
    bool Scanned,
    DateTimeOffset? CapturedAtUtc,
    bool Saved,
    bool InAd,
    HygieneDevice? Environment,
    string? GroupLabel,
    int? GroupTotal);

public sealed record ClientWorkspacePage(
    IReadOnlyList<ClientWorkspaceListItem> Items,
    int Total,
    int ScannedTotal,
    int SnapshotTotal,
    int Page,
    int PageSize,
    int? GroupCount,
    long SnapshotRevision,
    DateTimeOffset AssessedAtUtc,
    string? DomainName,
    HygieneSummary Summary,
    EnvironmentSourceStates Sources);

public sealed record ListClientWorkspaceRequest(
    DirectoryInventoryConnection? ActiveDirectory = null,
    KasperskyInventoryConnection? Kaspersky = null,
    string? Search = null,
    string? StatusFilter = null,
    string? SourceFilter = null,
    string? GroupMode = null,
    int Page = 1,
    int PageSize = 50,
    string? SortColumn = null,
    string? SortDirection = null,
    bool Force = false,
    string? OperationId = null);

internal static class ClientWorkspacePaging
{
    public static ClientWorkspacePage Page(
        ItHygieneResult result,
        IReadOnlyList<InventoryClientSnapshotHost> scannedHosts,
        IReadOnlyList<SavedClientTarget> savedClients,
        ListClientWorkspaceRequest request)
    {
        List<ClientWorkspaceEntry> merged = Merge(result.Devices, scannedHosts, savedClients);
        HygieneSummary workspaceSummary = ItHygieneService.Summarize(merged
            .Where(client => client.Environment is not null)
            .Select(client => client.Environment!)
            .ToList(), result.Sources);
        IEnumerable<ClientWorkspaceEntry> query = merged;

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            string search = request.Search.Trim();
            query = query.Where(client => SearchValues(client).Any(value =>
                value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true));
        }

        query = ApplyStatusFilter(query, request.StatusFilter, result.Sources);
        query = ApplySourceFilter(query, request.SourceFilter);
        List<ClientWorkspaceEntry> filtered = query.ToList();

        string groupMode = request.GroupMode?.ToUpperInvariant() ?? "NONE";
        Dictionary<string, int>? groupTotals = groupMode is "OS" or "SITE"
            ? filtered.GroupBy(client => GroupLabel(client, groupMode), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase)
            : null;

        IOrderedEnumerable<ClientWorkspaceEntry> ordered = ApplySort(
            filtered,
            groupMode,
            request.SortColumn,
            request.SortDirection);

        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize, 1, 100);
        List<ClientWorkspaceListItem> items;
        int? groupCount = null;
        if (groupMode is "OS" or "SITE")
        {
            List<IGrouping<string, ClientWorkspaceEntry>> groups = ordered
                .GroupBy(client => GroupLabel(client, groupMode), StringComparer.OrdinalIgnoreCase)
                .ToList();
            groupCount = groups.Count;
            items = groups
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .SelectMany(group => group)
                .Select(client => ToListItem(client, groupMode, groupTotals))
                .ToList();
        }
        else
        {
            items = ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(client => ToListItem(client, groupMode, groupTotals))
                .ToList();
        }

        return new ClientWorkspacePage(
            items,
            filtered.Count,
            filtered.Count(client => client.Scanned),
            merged.Count,
            page,
            pageSize,
            groupCount,
            result.SnapshotRevision,
            result.AssessedAtUtc,
            result.DomainName,
            workspaceSummary,
            result.Sources);
    }

    private static List<ClientWorkspaceEntry> Merge(
        IReadOnlyList<HygieneDevice> devices,
        IReadOnlyList<InventoryClientSnapshotHost> scannedHosts,
        IReadOnlyList<SavedClientTarget> savedClients)
    {
        var byKey = new Dictionary<string, ClientWorkspaceEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (HygieneDevice device in devices)
        {
            string host = device.HostName;
            byKey[ClientKey(host)] = new ClientWorkspaceEntry
            {
                Host = host,
                Key = ClientKey(host),
                Name = device.ComputerName,
                Os = device.ActiveDirectory.OperatingSystem,
                Description = device.ActiveDirectory.Description,
                Enabled = device.ActiveDirectory.Enabled ?? true,
                InAd = device.ActiveDirectory.Exists,
                Environment = device,
            };
        }

        foreach (InventoryClientSnapshotHost stored in scannedHosts)
        {
            if (string.IsNullOrWhiteSpace(stored.Host))
            {
                continue;
            }

            string key = ClientKey(stored.Host);
            if (!byKey.TryGetValue(key, out ClientWorkspaceEntry? client))
            {
                client = new ClientWorkspaceEntry
                {
                    Host = stored.Host,
                    Key = key,
                    Name = stored.Host,
                    Enabled = true,
                };
                byKey[key] = client;
            }

            client.Scanned = true;
            client.CapturedAtUtc = stored.CapturedAtUtc;
        }

        foreach (SavedClientTarget target in savedClients)
        {
            string key = ClientKey(target.Host);
            if (!byKey.TryGetValue(key, out ClientWorkspaceEntry? client))
            {
                client = new ClientWorkspaceEntry
                {
                    Host = target.Host,
                    Key = key,
                    Name = string.IsNullOrWhiteSpace(target.Label) ? target.Host : target.Label.Trim(),
                    Enabled = true,
                };
                byKey[key] = client;
            }

            client.Saved = true;
        }

        return byKey.Values.ToList();
    }

    private static IEnumerable<string?> SearchValues(ClientWorkspaceEntry client)
    {
        yield return client.Name;
        yield return client.Host;
        yield return client.Os;
        yield return client.Description;
        if (client.Environment is null)
        {
            yield break;
        }
        foreach (HygieneFinding finding in client.Environment.Assessment.Findings)
        {
            yield return finding.Message;
        }
    }

    private static IEnumerable<ClientWorkspaceEntry> ApplyStatusFilter(
        IEnumerable<ClientWorkspaceEntry> clients,
        string? filter,
        EnvironmentSourceStates sources)
    {
        string normalized = filter?.ToUpperInvariant() ?? "ALL";
        return normalized switch
        {
            "" or "ALL" => clients,
            "UNMANAGED" => clients.Where(client => client.Environment is null),
            _ => clients.Where(client => client.Environment is not null && ItHygienePaging.MatchesFilter(client.Environment, normalized, sources)),
        };
    }

    private static IEnumerable<ClientWorkspaceEntry> ApplySourceFilter(
        IEnumerable<ClientWorkspaceEntry> clients,
        string? filter) => filter?.ToUpperInvariant() switch
        {
            null or "" or "ALL" => clients,
            "SCANNED" => clients.Where(client => client.Scanned),
            "SAVED" => clients.Where(client => client.Saved),
            "AD" => clients.Where(client => client.Environment?.ActiveDirectory.Exists == true),
            "KASPERSKY" => clients.Where(client => client.Environment?.Kaspersky.Exists == true),
            "OPSI" => clients.Where(client => client.Environment?.Opsi.Exists == true),
            "NESSUS" => clients.Where(client => client.Environment?.Nessus.Exists == true),
            _ => Enumerable.Empty<ClientWorkspaceEntry>(),
        };

    private static IOrderedEnumerable<ClientWorkspaceEntry> ApplySort(
        IEnumerable<ClientWorkspaceEntry> clients,
        string groupMode,
        string? column,
        string? direction)
    {
        bool descending = string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase);
        Func<ClientWorkspaceEntry, object> key = string.Equals(column, "overall", StringComparison.OrdinalIgnoreCase)
            ? client => StatusRank(client.Environment?.Assessment.Status)
            : client => client.Name;

        if (groupMode is "OS" or "SITE")
        {
            IOrderedEnumerable<ClientWorkspaceEntry> grouped = clients
                .OrderBy(client => GroupLabel(client, groupMode), StringComparer.OrdinalIgnoreCase);
            return descending
                ? grouped.ThenByDescending(key).ThenBy(client => client.Name, StringComparer.OrdinalIgnoreCase)
                : grouped.ThenBy(key).ThenBy(client => client.Name, StringComparer.OrdinalIgnoreCase);
        }

        return descending
            ? clients.OrderByDescending(key).ThenBy(client => client.Name, StringComparer.OrdinalIgnoreCase)
            : clients.OrderBy(key).ThenBy(client => client.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static int StatusRank(HygieneStatus? status) => status switch
    {
        HygieneStatus.Critical => 5,
        HygieneStatus.CleanupCandidate => 4,
        HygieneStatus.Warning => 3,
        HygieneStatus.Incomplete => 2,
        HygieneStatus.Healthy => 1,
        _ => 0,
    };

    private static ClientWorkspaceListItem ToListItem(
        ClientWorkspaceEntry client,
        string groupMode,
        Dictionary<string, int>? groupTotals)
    {
        string? groupLabel = groupMode is "OS" or "SITE" ? GroupLabel(client, groupMode) : null;
        return new ClientWorkspaceListItem(
            client.Host,
            client.Key,
            client.Name,
            client.Os,
            client.Description,
            client.Enabled,
            client.Scanned,
            client.CapturedAtUtc,
            client.Saved,
            client.InAd,
            client.Environment,
            groupLabel,
            groupLabel is null ? null : groupTotals![groupLabel]);
    }

    private static string GroupLabel(ClientWorkspaceEntry client, string groupMode) =>
        groupMode == "OS" ? client.Os ?? "Unknown OS" : SiteOf(client.Name);

    private static string SiteOf(string name)
    {
        int dash = name.IndexOf('-');
        return dash <= 0 ? "Other" : name[..dash].Trim().ToUpperInvariant() is { Length: > 0 } site ? site : "Other";
    }

    private static string ClientKey(string host) =>
        host.Trim().Split('.')[0].ToUpperInvariant();

    private sealed class ClientWorkspaceEntry
    {
        public required string Host { get; init; }
        public required string Key { get; init; }
        public required string Name { get; init; }
        public string? Os { get; init; }
        public string? Description { get; init; }
        public bool Enabled { get; init; }
        public bool Scanned { get; set; }
        public DateTimeOffset? CapturedAtUtc { get; set; }
        public bool Saved { get; set; }
        public bool InAd { get; init; }
        public HygieneDevice? Environment { get; init; }
    }
}
