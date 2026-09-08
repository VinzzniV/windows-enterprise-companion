using Wec.Modules.PatchManagement.Domain;

namespace Wec.Modules.PatchManagement.Application;

public sealed record ListPatchClientStatesRequest(
    string? DepotFilter = null,
    string? ProductId = null,
    string? ClientSearch = null,
    string? ProductSearch = null,
    PatchWorkflowState? State = null,
    string? InstallationStatus = null,
    int Page = 1,
    int PageSize = 50,
    string? SortColumn = null,
    string? SortDirection = null);

internal static class PatchDashboardPaging
{
    public static PatchDashboardOverview Overview(PatchDashboardResult dashboard) => new(
        dashboard.ServerUrl,
        dashboard.DepotFilter,
        dashboard.GeneratedAtUtc,
        dashboard.Summary,
        dashboard.Depots,
        dashboard.Products.Select(ProductOverview).ToList());

    public static PatchClientStatePage Page(
        PatchDashboardResult dashboard,
        ListPatchClientStatesRequest request)
    {
        List<PatchClientListItem> snapshotItems = dashboard.Products.SelectMany(product =>
            product.Clients.Select(client => new PatchClientListItem(
                product.ProductId,
                product.Name,
                client))).ToList();
        IEnumerable<PatchClientListItem> query = snapshotItems;

        if (!string.IsNullOrWhiteSpace(request.ProductId))
        {
            query = query.Where(item => string.Equals(
                item.ProductId,
                request.ProductId.Trim(),
                StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(request.ClientSearch))
        {
            string search = request.ClientSearch.Trim();
            query = query.Where(item => ClientSearchValues(item.Client).Any(value =>
                value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true));
        }

        if (!string.IsNullOrWhiteSpace(request.ProductSearch))
        {
            string search = request.ProductSearch.Trim();
            query = query.Where(item =>
                item.ProductId.Contains(search, StringComparison.OrdinalIgnoreCase)
                || item.ProductName?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (request.State is { } state)
        {
            query = query.Where(item => item.Client.State == state);
        }

        if (!string.IsNullOrWhiteSpace(request.InstallationStatus))
        {
            query = query.Where(item => string.Equals(
                item.Client.InstallationStatus,
                request.InstallationStatus.Trim(),
                StringComparison.OrdinalIgnoreCase));
        }

        query = ApplySort(query, request.SortColumn, request.SortDirection);
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize, 1, 100);
        List<PatchClientListItem> all = query.ToList();
        return new PatchClientStatePage(
            all.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            all.Count,
            snapshotItems.Count,
            page,
            pageSize);
    }

    private static PatchProductOverviewRow ProductOverview(PatchProductRow product) => new(
        product.ProductId,
        product.Name,
        product.AvailableVersion,
        product.ReferenceVersion,
        product.DepotVersions,
        product.MissingDepotIds,
        product.PackageStatus,
        product.State,
        product.InstalledClientCount,
        product.OutdatedInstallationCount,
        product.FailedClientCount,
        product.PendingActionCount,
        product.LastError,
        product.WingetManaged,
        product.WingetId,
        product.LatestWingetVersion,
        product.WingetCheckStatus,
        product.WingetCheckedAtUtc,
        product.WingetCheckError,
        product.WingetUpdateAvailable);

    private static IEnumerable<string?> ClientSearchValues(PatchClientState client)
    {
        yield return client.ClientId;
        yield return client.DepotId;
        yield return client.InstalledVersion;
        yield return client.TargetVersion;
    }

    private static IOrderedEnumerable<PatchClientListItem> ApplySort(
        IEnumerable<PatchClientListItem> items,
        string? column,
        string? direction)
    {
        bool descending = string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase);
        Func<PatchClientListItem, string> value = column?.ToUpperInvariant() switch
        {
            "PRODUCT" => item => item.ProductName ?? item.ProductId,
            "DEPOT" => item => item.Client.DepotId ?? string.Empty,
            "INSTALLED" => item => item.Client.InstalledVersion ?? string.Empty,
            "TARGET" => item => item.Client.TargetVersion ?? string.Empty,
            "STATUS" => item => item.Client.State.ToString(),
            _ => item => item.Client.ClientId,
        };

        IOrderedEnumerable<PatchClientListItem> ordered = descending
            ? items.OrderByDescending(value, StringComparer.OrdinalIgnoreCase)
            : items.OrderBy(value, StringComparer.OrdinalIgnoreCase);
        return ordered
            .ThenBy(item => item.ProductId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Client.ClientId, StringComparer.OrdinalIgnoreCase);
    }
}
