using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Opsi;
using Wec.Core.RemoteExecution;
using Wec.Core.Results;
using Wec.Core.SoftwareUpdates;
using Wec.Modules.PatchManagement.Persistence;

namespace Wec.Modules.PatchManagement.Application;

public sealed record WingetPackagePreview(
    string OpsiProductId,
    string DisplayName,
    string WingetId,
    string WingetVersion,
    string Source,
    string Scope,
    string InstallerType,
    string Architecture,
    string DepotId,
    string? CurrentDepotVersion,
    string TargetDepotVersion,
    int PackageVersion,
    bool AdoptsExistingProduct,
    string WorkbenchPath,
    IReadOnlyList<string> Commands,
    string ConfirmationText,
    DateTimeOffset GeneratedAtUtc);

public sealed record WingetManagedPackageView(
    string OpsiProductId,
    string DisplayName,
    string WingetId,
    string Source,
    string Scope,
    string DepotId,
    string? CurrentDepotVersion,
    string? LastPackagedWingetVersion,
    string? LatestWingetVersion,
    bool UpdateAvailable,
    string CheckStatus,
    DateTimeOffset? CheckedAtUtc,
    string? LastError);

public sealed record WingetPackageOperationOutcome(
    string OpsiProductId,
    string DepotId,
    bool Success,
    string? OldVersion,
    string? NewVersion,
    string? Error);

public sealed record WingetUpdateCheckResult(
    int CheckedCount,
    int UpdateCount,
    int FailedCount,
    IReadOnlyList<WingetManagedPackageView> Packages);

public sealed record WingetUpdateSelection(string OpsiProductId, string ExpectedWingetVersion);

public sealed record WingetUpdatePlan(
    IReadOnlyList<WingetPackagePreview> Packages,
    string ConfirmationText,
    DateTimeOffset GeneratedAtUtc);

public sealed record WingetUpdateOutcome(
    int SucceededCount,
    int FailedCount,
    IReadOnlyList<WingetPackageOperationOutcome> Packages);

public sealed partial class WingetPackageService
{
    internal const string CreateAction = "WINGET_PACKAGE_CREATED";
    internal const string UpdateAction = "WINGET_PACKAGE_UPDATED";
    internal const string CheckAction = "WINGET_VERSION_CHECK";
    private readonly IWingetCatalogClient _catalogClient;
    private readonly IWingetManagedPackageRepository _repository;
    private readonly IOpsiClient _opsiClient;
    private readonly OpsiSessionState _sessionState;
    private readonly OpsiSessionConnector _sessionConnector;
    private readonly WingetPackageExecutor _executor;
    private readonly IPatchAuditRepository _auditRepository;
    private readonly IClock _clock;
    private readonly PatchManagementOptions _options;

    public WingetPackageService(
        IWingetCatalogClient catalogClient,
        IWingetManagedPackageRepository repository,
        IOpsiClient opsiClient,
        OpsiSessionState sessionState,
        OpsiSessionConnector sessionConnector,
        IRemoteFileUploader fileUploader,
        IRemoteCommandExecutor commandExecutor,
        IPatchAuditRepository auditRepository,
        IClock clock,
        IOptions<PatchManagementOptions> options)
    {
        _catalogClient = catalogClient;
        _repository = repository;
        _opsiClient = opsiClient;
        _sessionState = sessionState;
        _sessionConnector = sessionConnector;
        _executor = new WingetPackageExecutor(
            repository,
            opsiClient,
            sessionState,
            sessionConnector,
            fileUploader,
            commandExecutor,
            clock,
            options.Value);
        _auditRepository = auditRepository;
        _clock = clock;
        _options = options.Value;
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
            cancellationToken)
            .ConfigureAwait(false);
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
        if (!OpsiVersionPattern().IsMatch(package.Value.Version))
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
            .GetDepotsAsync(session.Value.Connection, cancellationToken)
            .ConfigureAwait(false);
        Result<IReadOnlyList<OpsiProduct>> products = await _opsiClient
            .GetProductsAsync(session.Value.Connection, cancellationToken)
            .ConfigureAwait(false);
        Result<IReadOnlyList<OpsiProductOnDepot>> depotProducts = await _opsiClient
            .GetProductsOnDepotsAsync(session.Value.Connection, cancellationToken)
            .ConfigureAwait(false);
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

        OpsiProductOnDepot? current = depotProducts.Value.FirstOrDefault(item =>
            string.Equals(item.ProductId, opsiProductId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.DepotId, depotId, StringComparison.OrdinalIgnoreCase));
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

    public async Task<Result<WingetPackageOperationOutcome>> CreateOrAdoptAsync(
        string opsiProductId,
        string displayName,
        string wingetId,
        string depotId,
        string expectedWingetVersion,
        bool confirmed,
        CancellationToken cancellationToken)
    {
        if (!confirmed)
        {
            return Result.Failure<WingetPackageOperationOutcome>(new Error(
                ErrorCode.InvalidRequest,
                "The Winget package operation must be explicitly confirmed after reviewing the preview."));
        }

        Result<WingetPackagePreview> preview = await PreviewAsync(
            opsiProductId,
            displayName,
            wingetId,
            depotId,
            cancellationToken).ConfigureAwait(false);
        if (preview.IsFailure)
        {
            return Result.Failure<WingetPackageOperationOutcome>(preview.Error!);
        }
        if (!string.Equals(preview.Value.WingetVersion, expectedWingetVersion, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<WingetPackageOperationOutcome>(new Error(
                ErrorCode.InvalidRequest,
                $"Winget now reports version '{preview.Value.WingetVersion}'. Review and confirm a new preview."));
        }

        Result<WingetPackageOperationOutcome> execution = await _executor.ExecuteSafelyAsync(
            preview.Value,
            cancellationToken).ConfigureAwait(false);
        await WriteOperationAuditAsync(preview.Value, execution, cancellationToken).ConfigureAwait(false);
        return execution;
    }

    public async Task<Result<IReadOnlyList<WingetManagedPackageView>>> ListAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WingetManagedPackage> records = await _repository.ListAsync(cancellationToken);
        IReadOnlyList<OpsiProductOnDepot> depotProducts = [];
        Result<OpsiSession?> ensured = await _sessionConnector.EnsureConnectedAsync(cancellationToken)
            .ConfigureAwait(false);
        if (ensured.IsSuccess && _sessionState.Current is { } session)
        {
            Result<IReadOnlyList<OpsiProductOnDepot>> read = await _opsiClient
                .GetProductsOnDepotsAsync(session.Connection, cancellationToken)
                .ConfigureAwait(false);
            if (read.IsSuccess)
            {
                depotProducts = read.Value;
            }
        }

        var views = new List<WingetManagedPackageView>(records.Count);
        foreach (WingetManagedPackage record in records)
        {
            OpsiProductOnDepot? current = FindDepotProduct(depotProducts, record.OpsiProductId, record.DepotId);
            bool updateAvailable = IsUpdateAvailable(record, current?.ProductVersion);
            views.Add(MapView(record, current, updateAvailable));
        }

        return Result.Success<IReadOnlyList<WingetManagedPackageView>>(views);
    }

    public async Task<Result<WingetUpdateCheckResult>> CheckUpdatesAsync(
        IReadOnlyList<string>? productIds,
        bool force,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WingetManagedPackage> all = await _repository.ListAsync(cancellationToken);
        var requested = new HashSet<string>(productIds ?? [], StringComparer.OrdinalIgnoreCase);
        WingetManagedPackage[] selected = [.. all.Where(record => requested.Count == 0
            || requested.Contains(record.OpsiProductId))];
        DateTimeOffset now = _clock.UtcNow;
        int checkedCount = 0;
        int failedCount = 0;
        foreach (WingetManagedPackage record in selected)
        {
            bool due = record.CheckedAtUtc is null
                || now - record.CheckedAtUtc >= _options.WingetCheckInterval;
            if (!force && !due)
            {
                continue;
            }

            checkedCount++;
            Result<WingetPackageInfo> package = await GetExactWingetAsync(
                record.WingetId,
                cancellationToken)
                .ConfigureAwait(false);
            bool packageable = package.IsSuccess
                && package.Value.IsEligible
                && OpsiVersionPattern().IsMatch(package.Value.Version);
            string? packageError = package.IsFailure
                ? package.Error!.Message
                : !package.Value.IsEligible
                    ? package.Value.IneligibilityReason
                    : !OpsiVersionPattern().IsMatch(package.Value.Version)
                        ? $"Winget version '{package.Value.Version}' cannot be represented as an opsi product version."
                        : null;
            WingetManagedPackage updated = package.IsSuccess
                ? record with
                {
                    LatestWingetVersion = package.Value.Version,
                    CheckStatus = packageable ? "SUCCESS" : "INELIGIBLE",
                    CheckedAtUtc = now,
                    LastError = packageError,
                    UpdatedAtUtc = now,
                }
                : record with
                {
                    CheckStatus = "FAILED",
                    CheckedAtUtc = now,
                    LastError = packageError,
                    UpdatedAtUtc = now,
                };
            if (!packageable)
            {
                failedCount++;
            }
            await _repository.UpsertAsync(updated, cancellationToken);
            await _auditRepository.AddAsync(new PatchAuditEntry(
                0,
                now,
                Environment.UserName,
                CheckAction,
                record.OpsiProductId,
                record.DepotId,
                [],
                JsonSerializer.Serialize(new { record.WingetId, updated.LatestWingetVersion }),
                updated.CheckStatus,
                updated.LastError,
                record.LastPackagedWingetVersion,
                updated.LatestWingetVersion), cancellationToken);
        }

        Result<IReadOnlyList<WingetManagedPackageView>> packages = await ListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (packages.IsFailure)
        {
            return Result.Failure<WingetUpdateCheckResult>(packages.Error!);
        }

        return Result.Success(new WingetUpdateCheckResult(
            checkedCount,
            packages.Value.Count(package => package.UpdateAvailable),
            failedCount,
            packages.Value));
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

    public async Task<Result<WingetUpdateOutcome>> ApplyUpdatesAsync(
        IReadOnlyList<WingetUpdateSelection> selections,
        bool confirmed,
        CancellationToken cancellationToken)
    {
        if (!confirmed)
        {
            return Result.Failure<WingetUpdateOutcome>(new Error(
                ErrorCode.InvalidRequest,
                "The Winget update batch must be explicitly confirmed."));
        }

        Result<WingetUpdatePlan> plan = await PrepareUpdatesAsync(selections, cancellationToken)
            .ConfigureAwait(false);
        if (plan.IsFailure)
        {
            return Result.Failure<WingetUpdateOutcome>(plan.Error!);
        }

        var outcomes = new List<WingetPackageOperationOutcome>(plan.Value.Packages.Count);
        foreach (WingetPackagePreview preview in plan.Value.Packages)
        {
            Result<WingetPackageOperationOutcome> result = await _executor.ExecuteSafelyAsync(
                preview,
                cancellationToken).ConfigureAwait(false);
            await WriteOperationAuditAsync(preview, result, cancellationToken).ConfigureAwait(false);
            outcomes.Add(result.IsSuccess
                ? result.Value
                : new WingetPackageOperationOutcome(
                    preview.OpsiProductId,
                    preview.DepotId,
                    false,
                    preview.CurrentDepotVersion,
                    null,
                    FormatError(result.Error!)));
        }

        return Result.Success(new WingetUpdateOutcome(
            outcomes.Count(outcome => outcome.Success),
            outcomes.Count(outcome => !outcome.Success),
            outcomes));
    }

    internal string BuildRemoteBuildCommand(WingetPackagePreview preview, string remoteArchive)
        => _executor.BuildRemoteBuildCommand(preview, remoteArchive);

    private async Task WriteOperationAuditAsync(
        WingetPackagePreview preview,
        Result<WingetPackageOperationOutcome> result,
        CancellationToken cancellationToken)
    {
        bool success = result.IsSuccess && result.Value.Success;
        await _auditRepository.AddAsync(new PatchAuditEntry(
            0,
            _clock.UtcNow,
            Environment.UserName,
            preview.AdoptsExistingProduct ? UpdateAction : CreateAction,
            preview.OpsiProductId,
            preview.DepotId,
            [],
            JsonSerializer.Serialize(preview),
            success ? "SUCCESS" : "FAILED",
            result.IsFailure ? FormatError(result.Error!) : result.Value.Error,
            preview.CurrentDepotVersion,
            success ? result.Value.NewVersion : null), cancellationToken);
    }

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

    private CancellationTokenSource CreateWingetTimeout(CancellationToken cancellationToken)
    {
        CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.WingetRequestTimeout);
        return timeout;
    }

    private async Task<Result<WingetPackageInfo>> GetExactWingetAsync(
        string wingetId,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CreateWingetTimeout(cancellationToken);
        return await _catalogClient.GetExactAsync(wingetId, timeout.Token).ConfigureAwait(false);
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

    private static bool IsUpdateAvailable(
        WingetManagedPackage record,
        string? currentDepotProductVersion)
    {
        if (record.CheckStatus != "SUCCESS"
            || string.IsNullOrWhiteSpace(record.LatestWingetVersion)
            || string.IsNullOrWhiteSpace(currentDepotProductVersion))
        {
            return false;
        }
        return CompareDotVersions(record.LatestWingetVersion, currentDepotProductVersion) > 0;
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

    private static string NormalizeVersionPart(string value)
    {
        string normalized = value.TrimStart('0');
        return normalized.Length == 0 ? "0" : normalized;
    }

    private static WingetManagedPackageView MapView(
        WingetManagedPackage record,
        OpsiProductOnDepot? current,
        bool updateAvailable) => new(
        record.OpsiProductId,
        record.DisplayName,
        record.WingetId,
        record.Source,
        record.Scope,
        record.DepotId,
        FormatVersion(current),
        record.LastPackagedWingetVersion,
        record.LatestWingetVersion,
        updateAvailable,
        record.CheckStatus,
        record.CheckedAtUtc,
        record.LastError);

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

    private static OpsiProductOnDepot? FindDepotProduct(
        IReadOnlyList<OpsiProductOnDepot> products,
        string productId,
        string depotId) => products.FirstOrDefault(item =>
            string.Equals(item.ProductId, productId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.DepotId, depotId, StringComparison.OrdinalIgnoreCase));

    private static string? FormatVersion(OpsiProductOnDepot? product) => product is null
        ? null
        : string.IsNullOrWhiteSpace(product.PackageVersion)
            ? product.ProductVersion
            : $"{product.ProductVersion}-{product.PackageVersion}";

    private static string FormatError(Error error) =>
        string.IsNullOrWhiteSpace(error.Details) ? error.Message : $"{error.Message} {error.Details}";

    [GeneratedRegex("^[a-z0-9][a-z0-9._+-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProductIdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._+-]{0,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex WingetIdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,252}$", RegexOptions.CultureInvariant)]
    private static partial Regex DepotIdPattern();

    [GeneratedRegex("^[0-9]+(?:\\.[0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex OpsiVersionPattern();
}
