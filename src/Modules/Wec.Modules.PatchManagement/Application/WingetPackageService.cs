using System.Text.Json;
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

public sealed class WingetPackageService
{
    internal const string CreateAction = "WINGET_PACKAGE_CREATED";
    internal const string UpdateAction = "WINGET_PACKAGE_UPDATED";
    internal const string CheckAction = "WINGET_VERSION_CHECK";
    private readonly WingetPackagePlanner _planner;
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
        _planner = new WingetPackagePlanner(
            catalogClient,
            repository,
            opsiClient,
            sessionState,
            sessionConnector,
            clock,
            options.Value);
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
        return await _planner.SearchAsync(query, limit, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<WingetPackagePreview>> PreviewAsync(
        string opsiProductId,
        string displayName,
        string wingetId,
        string depotId,
        CancellationToken cancellationToken)
    {
        return await _planner.PreviewAsync(
            opsiProductId,
            displayName,
            wingetId,
            depotId,
            cancellationToken).ConfigureAwait(false);
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
            Result<WingetPackageInfo> package = await _planner.GetExactWingetAsync(
                record.WingetId,
                cancellationToken)
                .ConfigureAwait(false);
            bool packageable = package.IsSuccess
                && package.Value.IsEligible
                && WingetPackagePlanner.IsRepresentableVersion(package.Value.Version);
            string? packageError = package.IsFailure
                ? package.Error!.Message
                : !package.Value.IsEligible
                    ? package.Value.IneligibilityReason
                    : !WingetPackagePlanner.IsRepresentableVersion(package.Value.Version)
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
        return await _planner.PrepareUpdatesAsync(selections, cancellationToken).ConfigureAwait(false);
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
        return WingetPackagePlanner.CompareDotVersions(record.LatestWingetVersion, currentDepotProductVersion) > 0;
    }

    internal static int CompareDotVersions(string left, string right) =>
        WingetPackagePlanner.CompareDotVersions(left, right);

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
        WingetPackagePlanner.FormatVersion(current),
        record.LastPackagedWingetVersion,
        record.LatestWingetVersion,
        updateAvailable,
        record.CheckStatus,
        record.CheckedAtUtc,
        record.LastError);

    internal static int DeterminePackageVersion(OpsiProductOnDepot? current, string productVersion) =>
        WingetPackagePlanner.DeterminePackageVersion(current, productVersion);

    private static OpsiProductOnDepot? FindDepotProduct(
        IReadOnlyList<OpsiProductOnDepot> products,
        string productId,
        string depotId) => WingetPackagePlanner.FindDepotProduct(products, productId, depotId);

    private static string FormatError(Error error) =>
        string.IsNullOrWhiteSpace(error.Details) ? error.Message : $"{error.Message} {error.Details}";

}
