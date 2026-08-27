using System.Text.RegularExpressions;
using Wec.Core.Abstractions;
using Wec.Core.Opsi;
using Wec.Core.Results;
using Wec.Core.SoftwareUpdates;
using Wec.Modules.PatchManagement.Persistence;

namespace Wec.Modules.PatchManagement.Application;

internal sealed partial class WingetPackagePlanner
{
    private readonly IWingetCatalogClient _catalogClient;
    private readonly IWingetManagedPackageRepository _repository;
    private readonly IOpsiClient _opsiClient;
    private readonly OpsiSessionState _sessionState;
    private readonly OpsiSessionConnector _sessionConnector;
    private readonly IClock _clock;
    private readonly PatchManagementOptions _options;

    public WingetPackagePlanner(
        IWingetCatalogClient catalogClient,
        IWingetManagedPackageRepository repository,
        IOpsiClient opsiClient,
        OpsiSessionState sessionState,
        OpsiSessionConnector sessionConnector,
        IClock clock,
        PatchManagementOptions options)
    {
        _catalogClient = catalogClient;
        _repository = repository;
        _opsiClient = opsiClient;
        _sessionState = sessionState;
        _sessionConnector = sessionConnector;
        _clock = clock;
        _options = options;
    }

    public async Task<Result<IReadOnlyList<WingetPackageInfo>>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CreateWingetTimeout(cancellationToken);
        return await _catalogClient.SearchAsync(query, limit, timeout.Token).ConfigureAwait(false);
    }

    public async Task<Result<WingetPackagePreview>> PreviewAsync(
        string opsiProductId,
        string displayName,
        string wingetId,
        string depotId,
        CancellationToken cancellationToken)
    {
        Result<bool> requestValidation = ValidatePackageRequest(
            opsiProductId,
            displayName,
            wingetId,
            depotId);
        if (requestValidation.IsFailure)
        {
            return Result.Failure<WingetPackagePreview>(requestValidation.Error!);
        }

        IReadOnlyList<WingetManagedPackage> managedPackages = await _repository
            .ListAsync(cancellationToken).ConfigureAwait(false);
        WingetManagedPackage? managedProduct = managedPackages.FirstOrDefault(item =>
            string.Equals(item.OpsiProductId, opsiProductId, StringComparison.OrdinalIgnoreCase));
        if (managedProduct is not null
            && (!string.Equals(managedProduct.WingetId, wingetId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(managedProduct.DepotId, depotId, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<WingetPackagePreview>(new Error(
                ErrorCode.InvalidRequest,
                $"The opsi product '{opsiProductId}' is already managed as Winget package '{managedProduct.WingetId}' on depot '{managedProduct.DepotId}'."));
        }
        WingetManagedPackage? duplicateWinget = managedPackages.FirstOrDefault(item =>
            !string.Equals(item.OpsiProductId, opsiProductId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.WingetId, wingetId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.DepotId, depotId, StringComparison.OrdinalIgnoreCase));
        if (duplicateWinget is not null)
        {
            return Result.Failure<WingetPackagePreview>(new Error(
                ErrorCode.InvalidRequest,
                $"Winget package '{wingetId}' is already managed as opsi product '{duplicateWinget.OpsiProductId}' on depot '{depotId}'."));
        }

        Result<WingetPackageInfo> package = await GetExactWingetAsync(
            wingetId,
            cancellationToken).ConfigureAwait(false);
        if (package.IsFailure)
        {
            return Result.Failure<WingetPackagePreview>(package.Error!);
        }
        if (!package.Value.IsEligible)
        {
            return Result.Failure<WingetPackagePreview>(new Error(
                ErrorCode.InvalidRequest,
                package.Value.IneligibilityReason ?? "The Winget package is not eligible for opsi/SYSTEM."));
        }
        if (!IsRepresentableVersion(package.Value.Version))
        {
            return Result.Failure<WingetPackagePreview>(new Error(
                ErrorCode.InvalidRequest,
                $"Winget version '{package.Value.Version}' cannot be represented as an opsi product version."));
        }

        Result<OpsiSession> session = await GetSessionAsync(cancellationToken).ConfigureAwait(false);
        if (session.IsFailure)
        {
            return Result.Failure<WingetPackagePreview>(session.Error!);
        }

        Result<IReadOnlyList<OpsiDepot>> depots = await _opsiClient
            .GetDepotsAsync(session.Value.Connection, cancellationToken).ConfigureAwait(false);
        Result<IReadOnlyList<OpsiProduct>> products = await _opsiClient
            .GetProductsAsync(session.Value.Connection, cancellationToken).ConfigureAwait(false);
        Result<IReadOnlyList<OpsiProductOnDepot>> depotProducts = await _opsiClient
            .GetProductsOnDepotsAsync(session.Value.Connection, cancellationToken).ConfigureAwait(false);
        Error? readError = new[] { depots.Error, products.Error, depotProducts.Error }
            .FirstOrDefault(error => error is not null);
        if (readError is not null)
        {
            return Result.Failure<WingetPackagePreview>(readError);
        }

        if (!depots.Value.Any(depot => string.Equals(depot.Id, depotId, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<WingetPackagePreview>(Error.NotFound($"Unknown opsi depot '{depotId}'."));
        }

        OpsiProductOnDepot? current = FindDepotProduct(depotProducts.Value, opsiProductId, depotId);
        if (current is not null)
        {
            Result<int> versionComparison = await CompareWingetVersionsAsync(
                package.Value.Id,
                package.Value.Version,
                current.ProductVersion,
                cancellationToken).ConfigureAwait(false);
            if (versionComparison.IsFailure)
            {
                return Result.Failure<WingetPackagePreview>(versionComparison.Error!);
            }
            if (versionComparison.Value < 0)
            {
                return Result.Failure<WingetPackagePreview>(new Error(
                    ErrorCode.InvalidRequest,
                    $"Winget version '{package.Value.Version}' is older than depot version '{current.ProductVersion}'. Downgrades are not allowed."));
            }
        }

        bool adoptsExisting = products.Value.Any(product =>
            string.Equals(product.Id, opsiProductId, StringComparison.OrdinalIgnoreCase));
        int packageVersion = DeterminePackageVersion(current, package.Value.Version);
        string? currentVersion = FormatVersion(current);
        string targetVersion = $"{package.Value.Version}-{packageVersion}";
        string workbench = $"{_options.WingetWorkbenchRoot.TrimEnd('/')}/{opsiProductId}";
        string[] commands =
        [
            $"winget install --id {package.Value.Id} --exact --source winget --version {package.Value.Version} --scope machine",
            $"winget upgrade --id {package.Value.Id} --exact --source winget --version {package.Value.Version} --scope machine",
            $"winget uninstall --id {package.Value.Id} --exact --source winget --scope machine",
        ];
        string confirmation = adoptsExisting
            ? $"Replace the package definition for existing opsi product '{opsiProductId}' on depot '{depotId}' with the generated Winget package {targetVersion}. Existing clients are not changed."
            : $"Create opsi product '{opsiProductId}' from Winget package '{package.Value.Id}' and install {targetVersion} on depot '{depotId}'.";

        return Result.Success(new WingetPackagePreview(
            opsiProductId,
            displayName.Trim(),
            package.Value.Id,
            package.Value.Version,
            package.Value.Source,
            "machine",
            package.Value.InstallerType,
            package.Value.Architecture,
            depotId,
            currentVersion,
            targetVersion,
            packageVersion,
            adoptsExisting,
            workbench,
            commands,
            confirmation,
            _clock.UtcNow));
    }

    public async Task<Result<WingetUpdatePlan>> PrepareUpdatesAsync(
        IReadOnlyList<WingetUpdateSelection> selections,
        CancellationToken cancellationToken)
    {
        if (selections.Count == 0)
        {
            return Result.Failure<WingetUpdatePlan>(new Error(
                ErrorCode.InvalidRequest,
                "Select at least one Winget package update."));
        }

        IReadOnlyList<WingetManagedPackage> records = await _repository.ListAsync(cancellationToken);
        var byId = records.ToDictionary(record => record.OpsiProductId, StringComparer.OrdinalIgnoreCase);
        var previews = new List<WingetPackagePreview>(selections.Count);
        foreach (WingetUpdateSelection selection in selections)
        {
            if (!byId.TryGetValue(selection.OpsiProductId, out WingetManagedPackage? record))
            {
                return Result.Failure<WingetUpdatePlan>(Error.NotFound(
                    $"Managed Winget product '{selection.OpsiProductId}' was not found."));
            }

            Result<WingetPackagePreview> preview = await PreviewAsync(
                record.OpsiProductId,
                record.DisplayName,
                record.WingetId,
                record.DepotId,
                cancellationToken).ConfigureAwait(false);
            if (preview.IsFailure)
            {
                return Result.Failure<WingetUpdatePlan>(preview.Error!);
            }
            if (!string.Equals(preview.Value.WingetVersion, selection.ExpectedWingetVersion, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<WingetUpdatePlan>(new Error(
                    ErrorCode.InvalidRequest,
                    $"Winget version for '{record.OpsiProductId}' changed. Refresh the update list."));
            }
            if (string.Equals(preview.Value.CurrentDepotVersion, preview.Value.TargetDepotVersion, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            previews.Add(preview.Value);
        }

        return previews.Count == 0
            ? Result.Failure<WingetUpdatePlan>(new Error(
                ErrorCode.InvalidRequest,
                "None of the selected packages needs a depot update."))
            : Result.Success(new WingetUpdatePlan(
                previews,
                $"Build and install {previews.Count} selected Winget package update(s) on their configured opsi depots. No client action requests will be changed.",
                _clock.UtcNow));
    }

    public async Task<Result<WingetPackageInfo>> GetExactWingetAsync(
        string wingetId,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CreateWingetTimeout(cancellationToken);
        return await _catalogClient.GetExactAsync(wingetId, timeout.Token).ConfigureAwait(false);
    }

    internal static bool IsRepresentableVersion(string version) => OpsiVersionPattern().IsMatch(version);

    internal static int DeterminePackageVersion(OpsiProductOnDepot? current, string productVersion)
    {
        if (current is null
            || !string.Equals(current.ProductVersion, productVersion, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }
        return int.TryParse(current.PackageVersion, out int packageVersion)
            ? checked(packageVersion + 1)
            : 1;
    }

    internal static int CompareDotVersions(string left, string right)
    {
        string[] leftParts = left.Split('.');
        string[] rightParts = right.Split('.');
        for (int index = 0; index < Math.Max(leftParts.Length, rightParts.Length); index++)
        {
            string leftPart = NormalizeVersionPart(index < leftParts.Length ? leftParts[index] : "0");
            string rightPart = NormalizeVersionPart(index < rightParts.Length ? rightParts[index] : "0");
            int lengthComparison = leftPart.Length.CompareTo(rightPart.Length);
            if (lengthComparison != 0)
            {
                return lengthComparison;
            }
            int valueComparison = string.CompareOrdinal(leftPart, rightPart);
            if (valueComparison != 0)
            {
                return valueComparison;
            }
        }
        return 0;
    }

    internal static OpsiProductOnDepot? FindDepotProduct(
        IReadOnlyList<OpsiProductOnDepot> products,
        string productId,
        string depotId) => products.FirstOrDefault(item =>
            string.Equals(item.ProductId, productId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.DepotId, depotId, StringComparison.OrdinalIgnoreCase));

    internal static string? FormatVersion(OpsiProductOnDepot? product) => product is null
        ? null
        : string.IsNullOrWhiteSpace(product.PackageVersion)
            ? product.ProductVersion
            : $"{product.ProductVersion}-{product.PackageVersion}";

    private async Task<Result<OpsiSession>> GetSessionAsync(CancellationToken cancellationToken)
    {
        Result<OpsiSession?> ensured = await _sessionConnector.EnsureConnectedAsync(cancellationToken)
            .ConfigureAwait(false);
        if (ensured.IsFailure)
        {
            return Result.Failure<OpsiSession>(ensured.Error!);
        }
        return _sessionState.Current is { } session
            ? Result.Success(session)
            : Result.Failure<OpsiSession>(PatchDashboardService.NotConnected);
    }

    private async Task<Result<int>> CompareWingetVersionsAsync(
        string wingetId,
        string leftVersion,
        string rightVersion,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CreateWingetTimeout(cancellationToken);
        return await _catalogClient.CompareVersionsAsync(
            wingetId,
            leftVersion,
            rightVersion,
            timeout.Token).ConfigureAwait(false);
    }

    private CancellationTokenSource CreateWingetTimeout(CancellationToken cancellationToken)
    {
        CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.WingetRequestTimeout);
        return timeout;
    }

    private static Result<bool> ValidatePackageRequest(
        string productId,
        string displayName,
        string wingetId,
        string depotId)
    {
        if (!ProductIdPattern().IsMatch(productId)
            || string.IsNullOrWhiteSpace(displayName)
            || displayName.Length > 256
            || !WingetIdPattern().IsMatch(wingetId)
            || !DepotIdPattern().IsMatch(depotId))
        {
            return Result.Failure<bool>(new Error(
                ErrorCode.InvalidRequest,
                "The opsi product id, display name, Winget id or depot id is invalid."));
        }
        return Result.Success(true);
    }

    private static string NormalizeVersionPart(string value)
    {
        string normalized = value.TrimStart('0');
        return normalized.Length == 0 ? "0" : normalized;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._+-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProductIdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._+-]{0,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex WingetIdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,252}$", RegexOptions.CultureInvariant)]
    private static partial Regex DepotIdPattern();

    [GeneratedRegex("^[0-9]+(?:\\.[0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex OpsiVersionPattern();
}
