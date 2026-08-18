using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Opsi;
using Wec.Core.RemoteExecution;
using Wec.Core.Results;
using Wec.Modules.PatchManagement.Domain;
using Wec.Modules.PatchManagement.Persistence;

namespace Wec.Modules.PatchManagement.Application;

public sealed record RolloutPreviewClient(
    string ClientId,
    string? DepotId,
    string? InstalledVersion,
    string? TargetVersion,
    PatchWorkflowState CurrentState);

public sealed record RolloutPreview(
    string ProductId,
    string? ProductName,
    string? DepotFilter,
    string PlannedAction,
    IReadOnlyList<RolloutPreviewClient> Clients,
    DateTimeOffset GeneratedAtUtc);

public sealed record RolloutRequestOutcome(int RequestedClientCount);

public sealed record PackageUpdateTarget(
    string DepotId,
    string Host,
    string? CurrentVersion,
    string Command);

public sealed record PreparePackagesPlan(
    string ProductId,
    string Stage,
    string Mode,
    string? ArtifactVersion,
    IReadOnlyList<PackageUpdateTarget> Targets,
    string Note,
    string ConfirmationText,
    DateTimeOffset GeneratedAtUtc);

public sealed record PackageUpdateTargetOutcome(
    string DepotId,
    bool Success,
    int? ExitCode,
    string? OldVersion,
    string? NewVersion,
    string? Error);

public sealed record PackageUpdateOutcome(
    string ProductId,
    string Stage,
    int SucceededTargetCount,
    int FailedTargetCount,
    IReadOnlyList<PackageUpdateTargetOutcome> Targets);

public sealed record PackageWorkflowStatus(
    string ProductId,
    string? TestDepotId,
    string? TestedVersion,
    DateTimeOffset? TestUpdateSucceededAtUtc,
    DateTimeOffset? PilotApprovedAtUtc,
    bool PilotApproved,
    string? LastSynchronizationResult,
    DateTimeOffset? LastSynchronizationAtUtc,
    string? LastError);

/// <summary>
/// Every action goes preview → confirm → execute and always leaves an audit
/// entry — including failed and merely planned ones (ADR 0008).
/// </summary>
public sealed partial class PatchActionService
{
    internal const string RolloutAction = "ROLLOUT_REQUESTED";
    internal const string PreparePackagesAction = "PACKAGE_UPDATE_PLANNED";
    internal const string TestPackageUpdateAction = "PACKAGE_TEST_UPDATED";
    internal const string PilotApprovedAction = "PACKAGE_PILOT_APPROVED";
    internal const string DepotSynchronizationAction = "PACKAGE_DEPOT_SYNCHRONIZED";
    public const string TestStage = "TEST";
    public const string DepotSynchronizationStage = "DEPOT_SYNC";

    private readonly IOpsiClient _opsiClient;
    private readonly OpsiSessionState _sessionState;
    private readonly OpsiSessionConnector _sessionConnector;
    private readonly PatchDashboardService _dashboardService;
    private readonly IPatchAuditRepository _auditRepository;
    private readonly IRemoteCommandExecutor _remoteCommandExecutor;
    private readonly IRemoteArtifactStager _remoteArtifactStager;
    private readonly IProductVersionSourceRepository _versionSourceRepository;
    private readonly IClock _clock;
    private readonly PatchManagementOptions _options;

    public PatchActionService(
        IOpsiClient opsiClient,
        OpsiSessionState sessionState,
        OpsiSessionConnector sessionConnector,
        PatchDashboardService dashboardService,
        IPatchAuditRepository auditRepository,
        IRemoteCommandExecutor remoteCommandExecutor,
        IRemoteArtifactStager remoteArtifactStager,
        IProductVersionSourceRepository versionSourceRepository,
        IClock clock,
        IOptions<PatchManagementOptions> options)
    {
        _opsiClient = opsiClient;
        _sessionState = sessionState;
        _sessionConnector = sessionConnector;
        _dashboardService = dashboardService;
        _auditRepository = auditRepository;
        _remoteCommandExecutor = remoteCommandExecutor;
        _remoteArtifactStager = remoteArtifactStager;
        _versionSourceRepository = versionSourceRepository;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<Result<RolloutPreview>> BuildRolloutPreviewAsync(
        string productId,
        string? depotFilter,
        IReadOnlyList<string>? clientIds,
        CancellationToken cancellationToken)
    {
        Result<PatchDashboardResult> dashboard =
            await _dashboardService.GetDashboardAsync(depotFilter, cancellationToken);
        if (dashboard.IsFailure)
        {
            return Result.Failure<RolloutPreview>(dashboard.Error!);
        }

        PatchProductRow? product = dashboard.Value.Products
            .FirstOrDefault(row => string.Equals(row.ProductId, productId, StringComparison.OrdinalIgnoreCase));
        if (product is null)
        {
            return Result.Failure<RolloutPreview>(Error.NotFound(
                $"Product '{productId}' is not available on the selected depot."));
        }

        List<PatchClientState> targets;
        if (clientIds is { Count: > 0 })
        {
            var requested = new HashSet<string>(clientIds, StringComparer.OrdinalIgnoreCase);
            targets = [.. product.Clients.Where(client => requested.Contains(client.ClientId))];
            if (targets.Count != requested.Count)
            {
                IEnumerable<string> unknown = requested.Except(
                    targets.Select(client => client.ClientId), StringComparer.OrdinalIgnoreCase);
                return Result.Failure<RolloutPreview>(new Error(
                    ErrorCode.InvalidRequest,
                    $"Unknown clients for product '{productId}': {string.Join(", ", unknown)}."));
            }
        }
        else
        {
            // Default target set: everything that needs the update or failed on it
            targets = [.. product.Clients.Where(client =>
                client.State is PatchWorkflowState.UpdateAvailable or PatchWorkflowState.Failed)];
        }

        return Result.Success(new RolloutPreview(
            product.ProductId,
            product.Name,
            dashboard.Value.DepotFilter,
            "setup",
            [.. targets.Select(client => new RolloutPreviewClient(
                client.ClientId, client.DepotId, client.InstalledVersion, client.TargetVersion, client.State))],
            _clock.UtcNow));
    }

    public async Task<Result<RolloutRequestOutcome>> RequestRolloutAsync(
        string productId,
        IReadOnlyList<string> clientIds,
        string? depotFilter,
        bool confirmed,
        CancellationToken cancellationToken)
    {
        if (!confirmed)
        {
            return Result.Failure<RolloutRequestOutcome>(new Error(
                ErrorCode.InvalidRequest,
                "A rollout request must be explicitly confirmed after reviewing the preview."));
        }

        Result<OpsiSession?> ensured = await _sessionConnector.EnsureConnectedAsync(cancellationToken);
        if (ensured.IsFailure)
        {
            return Result.Failure<RolloutRequestOutcome>(ensured.Error!);
        }
        if (_sessionState.Current is not { } session)
        {
            return Result.Failure<RolloutRequestOutcome>(PatchDashboardService.NotConnected);
        }

        Result<RolloutPreview> preview =
            await BuildRolloutPreviewAsync(productId, depotFilter, clientIds, cancellationToken);
        if (preview.IsFailure)
        {
            return Result.Failure<RolloutRequestOutcome>(preview.Error!);
        }

        if (preview.Value.Clients.Count == 0)
        {
            return Result.Failure<RolloutRequestOutcome>(new Error(
                ErrorCode.InvalidRequest, "The rollout preview contains no target clients."));
        }

        string[] targets = [.. preview.Value.Clients.Select(client => client.ClientId)];
        Result<int> rollout = await _opsiClient.RequestSetupAsync(
            session.Connection, preview.Value.ProductId, targets, cancellationToken);

        await _auditRepository.AddAsync(
            new PatchAuditEntry(
                Id: 0,
                _clock.UtcNow,
                Environment.UserName,
                RolloutAction,
                preview.Value.ProductId,
                depotFilter,
                targets,
                JsonSerializer.Serialize(preview.Value),
                rollout.IsSuccess ? "SUCCESS" : "FAILED",
                rollout.Error?.Message),
            cancellationToken);

        return rollout.IsSuccess
            ? Result.Success(new RolloutRequestOutcome(rollout.Value))
            : Result.Failure<RolloutRequestOutcome>(rollout.Error!);
    }

    public async Task<Result<PreparePackagesPlan>> BuildPackageUpdatePreviewAsync(
        string productId,
        string stage,
        IReadOnlyList<string> depotIds,
        CancellationToken cancellationToken,
        bool writeAudit = true)
    {
        Result<OpsiSession?> ensured = await _sessionConnector.EnsureConnectedAsync(cancellationToken);
        if (ensured.IsFailure)
        {
            return Result.Failure<PreparePackagesPlan>(ensured.Error!);
        }
        if (_sessionState.Current is not { } session)
        {
            return Result.Failure<PreparePackagesPlan>(PatchDashboardService.NotConnected);
        }

        if (!ProductIdPattern().IsMatch(productId))
        {
            return Result.Failure<PreparePackagesPlan>(new Error(
                ErrorCode.InvalidRequest, "The opsi product id contains unsupported characters."));
        }

        string normalizedStage = stage.Trim().ToUpperInvariant();
        if (normalizedStage is not (TestStage or DepotSynchronizationStage))
        {
            return Result.Failure<PreparePackagesPlan>(new Error(
                ErrorCode.InvalidRequest, $"Unknown package update stage '{stage}'."));
        }

        string[] requestedDepotIds = [.. depotIds
            .Where(depotId => !string.IsNullOrWhiteSpace(depotId))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        if (requestedDepotIds.Length == 0
            || (normalizedStage == TestStage && requestedDepotIds.Length != 1))
        {
            return Result.Failure<PreparePackagesPlan>(new Error(
                ErrorCode.InvalidRequest,
                normalizedStage == TestStage
                    ? "A test update must target exactly one depot."
                    : "Select at least one depot to synchronize."));
        }

        Result<IReadOnlyList<OpsiDepot>> depots =
            await _opsiClient.GetDepotsAsync(session.Connection, cancellationToken);
        if (depots.IsFailure)
        {
            return Result.Failure<PreparePackagesPlan>(depots.Error!);
        }

        Result<IReadOnlyList<OpsiProduct>> products =
            await _opsiClient.GetProductsAsync(session.Connection, cancellationToken);
        if (products.IsFailure)
        {
            return Result.Failure<PreparePackagesPlan>(products.Error!);
        }

        if (!products.Value.Any(product => string.Equals(product.Id, productId, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<PreparePackagesPlan>(Error.NotFound(
                $"Product '{productId}' is not known to the connected opsi service."));
        }

        var depotById = depots.Value.ToDictionary(depot => depot.Id, StringComparer.OrdinalIgnoreCase);
        string[] unknownDepots = [.. requestedDepotIds.Where(depotId => !depotById.ContainsKey(depotId))];
        if (unknownDepots.Length > 0)
        {
            return Result.Failure<PreparePackagesPlan>(Error.NotFound(
                $"Unknown opsi depot(s): {string.Join(", ", unknownDepots)}."));
        }

        PackageWorkflowStatus workflow = await GetPackageWorkflowStatusAsync(productId, cancellationToken);
        if (normalizedStage == DepotSynchronizationStage)
        {
            if (!workflow.PilotApproved || workflow.TestDepotId is null)
            {
                return Result.Failure<PreparePackagesPlan>(new Error(
                    ErrorCode.InvalidRequest,
                    "Depot synchronization is locked until a successful test-depot update has been explicitly approved."));
            }

            if (requestedDepotIds.Contains(workflow.TestDepotId, StringComparer.OrdinalIgnoreCase))
            {
                return Result.Failure<PreparePackagesPlan>(new Error(
                    ErrorCode.InvalidRequest,
                    "The approved test depot must not be included in the production synchronization targets."));
            }
        }

        Result<IReadOnlyList<OpsiProductOnDepot>> versions =
            await _opsiClient.GetProductsOnDepotsAsync(session.Connection, cancellationToken);
        if (versions.IsFailure)
        {
            return Result.Failure<PreparePackagesPlan>(versions.Error!);
        }

        Result<(string Mode, string? ArtifactVersion, string Command)> packageOperation =
            await BuildPackageOperationAsync(productId, normalizedStage, cancellationToken);
        if (packageOperation.IsFailure)
        {
            return Result.Failure<PreparePackagesPlan>(packageOperation.Error!);
        }

        PackageUpdateTarget[] targets = [.. requestedDepotIds
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(depotId => new PackageUpdateTarget(
                depotId,
                depotById[depotId].Id,
                FindVersion(versions.Value, productId, depotId),
                packageOperation.Value.Command))];
        var plan = new PreparePackagesPlan(
            productId,
            normalizedStage,
            packageOperation.Value.Mode,
            packageOperation.Value.ArtifactVersion,
            targets,
            packageOperation.Value.Mode == "CUSTOM_BUILD"
                ? $"WEC downloads manufacturer version {packageOperation.Value.ArtifactVersion}, uploads it under a stable installer name, updates control.toml, builds the opsi package and installs it only on the selected test depot."
                : packageOperation.Value.Mode == "CUSTOM_PROMOTION"
                    ? $"WEC transfers the exact approved test artifact ({packageOperation.Value.ArtifactVersion}) from the test depot and installs that unchanged package on every selected depot."
                : normalizedStage == TestStage
                ? "The repository-backed package is updated only on the selected test depot. SSH uses a trusted host key and key/agent authentication."
                : "The approved package is pulled and installed on every listed depot. Failed depots do not stop the remaining targets and are recorded separately.",
            normalizedStage == TestStage
                ? $"I have reviewed the command and want to update '{productId}' on test depot '{targets[0].DepotId}'."
                : $"The pilot was approved. I want to synchronize '{productId}' to {targets.Length} additional depot(s).",
            _clock.UtcNow);

        if (writeAudit)
        {
            await _auditRepository.AddAsync(
                new PatchAuditEntry(
                    Id: 0,
                    _clock.UtcNow,
                    Environment.UserName,
                    PreparePackagesAction,
                    productId,
                    DepotId: normalizedStage == TestStage ? targets[0].DepotId : null,
                    TargetClients: [],
                    JsonSerializer.Serialize(plan),
                    "PLANNED",
                    ErrorMessage: null),
                cancellationToken);
        }

        return Result.Success(plan);
    }

    public async Task<Result<PackageUpdateOutcome>> ExecutePackageUpdateAsync(
        string productId,
        string stage,
        IReadOnlyList<string> depotIds,
        bool confirmed,
        CancellationToken cancellationToken)
    {
        if (!confirmed)
        {
            return Result.Failure<PackageUpdateOutcome>(new Error(
                ErrorCode.InvalidRequest,
                "A package operation must be explicitly confirmed after reviewing the preview."));
        }

        Result<PreparePackagesPlan> preview =
            await BuildPackageUpdatePreviewAsync(
                productId, stage, depotIds, cancellationToken, writeAudit: false);
        if (preview.IsFailure)
        {
            return Result.Failure<PackageUpdateOutcome>(preview.Error!);
        }

        var executions = new List<(
            PackageUpdateTarget Target,
            Result<RemoteCommandResult> Result,
            object? Artifact)>();
        foreach (PackageUpdateTarget target in preview.Value.Targets)
        {
            object? artifact = null;
            if (preview.Value.Mode == "CUSTOM_BUILD")
            {
                Result<RemoteArtifactStageResult> staging = await StagePackageArtifactAsync(
                    productId,
                    preview.Value.ArtifactVersion!,
                    target,
                    cancellationToken);
                if (staging.IsFailure)
                {
                    executions.Add((target, Result.Failure<RemoteCommandResult>(staging.Error!), null));
                    continue;
                }

                artifact = staging.Value;
            }
            else if (preview.Value.Mode == "CUSTOM_PROMOTION")
            {
                PackageWorkflowStatus workflow = await GetPackageWorkflowStatusAsync(
                    productId,
                    cancellationToken);
                Result<RemoteArtifactTransferResult> transfer = await _remoteArtifactStager.TransferAsync(
                    new RemoteArtifactTransferRequest(
                        workflow.TestDepotId!,
                        target.Host,
                        _options.SshUserName,
                        BuildApprovedPackagePath(productId),
                        _options.SshConnectTimeout,
                        _options.PackageTransferTimeout,
                        _options.SshIdentityFile),
                    cancellationToken);
                if (transfer.IsFailure)
                {
                    executions.Add((target, Result.Failure<RemoteCommandResult>(transfer.Error!), null));
                    continue;
                }

                artifact = transfer.Value;
            }

            Result<RemoteCommandResult> execution = await _remoteCommandExecutor.ExecuteAsync(
                new RemoteCommandRequest(
                    target.Host,
                    _options.SshUserName,
                    target.Command,
                    _options.SshConnectTimeout,
                    _options.PackageCommandTimeout,
                    _options.SshIdentityFile),
                cancellationToken);
            executions.Add((target, execution, artifact));
        }

        Result<IReadOnlyList<OpsiProductOnDepot>>? refreshedVersions = null;
        if (executions.Any(execution => execution.Result.IsSuccess)
            && _sessionState.Current is { } session)
        {
            refreshedVersions = await _opsiClient.GetProductsOnDepotsAsync(
                session.Connection, cancellationToken);
        }

        var outcomes = new List<PackageUpdateTargetOutcome>();
        string auditAction = preview.Value.Stage == TestStage
            ? TestPackageUpdateAction
            : DepotSynchronizationAction;
        foreach ((PackageUpdateTarget target, Result<RemoteCommandResult> execution, object? artifact) in executions)
        {
            string? newVersion = refreshedVersions is { IsSuccess: true }
                ? FindVersion(refreshedVersions.Value, productId, target.DepotId)
                : null;
            string? verificationError = execution.IsSuccess && refreshedVersions is { IsFailure: true }
                ? $"The command succeeded, but opsi version verification failed: {refreshedVersions.Error!.Message}"
                : execution.IsSuccess && newVersion is null
                    ? "The command succeeded, but the product is still missing from the target depot."
                    : null;
            string? executionError = execution.IsFailure
                ? FormatError(execution.Error!)
                : verificationError;
            bool success = execution.IsSuccess && executionError is null;
            var outcome = new PackageUpdateTargetOutcome(
                target.DepotId,
                success,
                execution.IsSuccess ? execution.Value.ExitCode : null,
                target.CurrentVersion,
                newVersion,
                executionError);
            outcomes.Add(outcome);

            await _auditRepository.AddAsync(new PatchAuditEntry(
                Id: 0,
                _clock.UtcNow,
                Environment.UserName,
                auditAction,
                productId,
                target.DepotId,
                TargetClients: [],
                JsonSerializer.Serialize(new
                {
                    Preview = target,
                    Output = execution.IsSuccess ? execution.Value.StandardOutput : null,
                    StandardError = execution.IsSuccess ? execution.Value.StandardError : execution.Error?.Details,
                    Duration = execution.IsSuccess ? (TimeSpan?)execution.Value.Duration : null,
                    Artifact = artifact,
                }),
                success ? "SUCCESS" : "FAILED",
                executionError,
                target.CurrentVersion,
                newVersion), cancellationToken);
        }

        return Result.Success(new PackageUpdateOutcome(
            productId,
            preview.Value.Stage,
            outcomes.Count(outcome => outcome.Success),
            outcomes.Count(outcome => !outcome.Success),
            outcomes));
    }

    public async Task<Result<PackageWorkflowStatus>> ApprovePackagePilotAsync(
        string productId,
        bool confirmed,
        CancellationToken cancellationToken)
    {
        if (!confirmed)
        {
            return Result.Failure<PackageWorkflowStatus>(new Error(
                ErrorCode.InvalidRequest, "Pilot approval must be explicitly confirmed."));
        }

        PatchAuditEntry? testUpdate = await _auditRepository.FindLatestAsync(
            productId, TestPackageUpdateAction, cancellationToken);
        if (testUpdate is null || testUpdate.Result != "SUCCESS" || testUpdate.DepotId is null)
        {
            return Result.Failure<PackageWorkflowStatus>(new Error(
                ErrorCode.InvalidRequest,
                "Pilot approval requires a successful latest test-depot package update."));
        }

        Result<OpsiSession?> ensured = await _sessionConnector.EnsureConnectedAsync(cancellationToken);
        if (ensured.IsFailure)
        {
            return Result.Failure<PackageWorkflowStatus>(ensured.Error!);
        }
        if (_sessionState.Current is not { } session)
        {
            return Result.Failure<PackageWorkflowStatus>(PatchDashboardService.NotConnected);
        }

        Result<IReadOnlyList<OpsiDepot>> depots =
            await _opsiClient.GetDepotsAsync(session.Connection, cancellationToken);
        Result<IReadOnlyList<OpsiClientHost>> clients =
            await _opsiClient.GetClientsAsync(session.Connection, cancellationToken);
        Result<IReadOnlyList<OpsiProductOnClient>> states =
            await _opsiClient.GetProductStatesAsync(session.Connection, cancellationToken);
        Error? readError = new[] { depots.Error, clients.Error, states.Error }
            .FirstOrDefault(error => error is not null);
        if (readError is not null)
        {
            return Result.Failure<PackageWorkflowStatus>(readError);
        }

        string? configServerId = depots.Value.FirstOrDefault(depot => depot.IsConfigServer)?.Id;
        var pilotClientIds = new HashSet<string>(clients.Value
            .Where(client => string.Equals(
                client.DepotId ?? configServerId,
                testUpdate.DepotId,
                StringComparison.OrdinalIgnoreCase))
            .Select(client => client.Id), StringComparer.OrdinalIgnoreCase);
        string[] verifiedPilotClients = [.. states.Value
            .Where(state => pilotClientIds.Contains(state.ClientId)
                && string.Equals(state.ProductId, productId, StringComparison.OrdinalIgnoreCase)
                && state.InstallationStatus == "installed"
                && state.ActionRequest is null or "none"
                && state.ActionResult != "failed"
                && string.Equals(
                    FormatVersion(state.InstalledProductVersion, state.InstalledPackageVersion),
                    testUpdate.NewVersion,
                    StringComparison.OrdinalIgnoreCase))
            .Select(state => state.ClientId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];
        if (verifiedPilotClients.Length == 0)
        {
            return Result.Failure<PackageWorkflowStatus>(new Error(
                ErrorCode.InvalidRequest,
                $"No successfully updated pilot client on test depot '{testUpdate.DepotId}' reports version '{testUpdate.NewVersion}'."));
        }

        await _auditRepository.AddAsync(new PatchAuditEntry(
            Id: 0,
            _clock.UtcNow,
            Environment.UserName,
            PilotApprovedAction,
            productId,
            testUpdate.DepotId,
            TargetClients: verifiedPilotClients,
            JsonSerializer.Serialize(new
            {
                TestUpdateAtUtc = testUpdate.TimestampUtc,
                TestDepotId = testUpdate.DepotId,
                TestedVersion = testUpdate.NewVersion,
            }),
            "SUCCESS",
            ErrorMessage: null,
            testUpdate.OldVersion,
            testUpdate.NewVersion), cancellationToken);

        return Result.Success(await GetPackageWorkflowStatusAsync(productId, cancellationToken));
    }

    public async Task<PackageWorkflowStatus> GetPackageWorkflowStatusAsync(
        string productId,
        CancellationToken cancellationToken)
    {
        PatchAuditEntry? testUpdate = await _auditRepository.FindLatestAsync(
            productId, TestPackageUpdateAction, cancellationToken);
        PatchAuditEntry? approval = await _auditRepository.FindLatestAsync(
            productId, PilotApprovedAction, cancellationToken);
        PatchAuditEntry? synchronization = await _auditRepository.FindLatestAsync(
            productId, DepotSynchronizationAction, cancellationToken);
        bool testSucceeded = testUpdate?.Result == "SUCCESS";
        bool approved = testSucceeded
            && approval?.Result == "SUCCESS"
            && approval.TimestampUtc >= testUpdate!.TimestampUtc
            && string.Equals(approval.DepotId, testUpdate.DepotId, StringComparison.OrdinalIgnoreCase);
        PatchAuditEntry? latestOperation = new[] { testUpdate, synchronization }
            .Where(entry => entry is not null)
            .OrderByDescending(entry => entry!.TimestampUtc)
            .FirstOrDefault();

        return new PackageWorkflowStatus(
            productId,
            testUpdate?.DepotId,
            testSucceeded ? testUpdate?.NewVersion : null,
            testSucceeded ? testUpdate?.TimestampUtc : null,
            approved ? approval?.TimestampUtc : null,
            approved,
            synchronization?.Result,
            synchronization?.TimestampUtc,
            latestOperation?.Result == "FAILED" ? latestOperation.ErrorMessage : null);
    }

    private string BuildUpdaterCommand(string productId)
    {
        string prefix = _options.UseNonInteractiveSudo ? "sudo -n " : string.Empty;
        return $"{prefix}opsi-package-updater -v update {productId}";
    }

    private async Task<Result<(string Mode, string? ArtifactVersion, string Command)>> BuildPackageOperationAsync(
        string productId,
        string stage,
        CancellationToken cancellationToken)
    {
        if (!_options.PackageAutomationProfiles.TryGetValue(productId, out PackageAutomationProfileOptions? profile))
        {
            return Result.Success<(string Mode, string? ArtifactVersion, string Command)>(
                ("REPOSITORY", null, BuildUpdaterCommand(productId)));
        }

        if (stage != TestStage)
        {
            PackageWorkflowStatus workflow = await GetPackageWorkflowStatusAsync(productId, cancellationToken);
            if (!workflow.PilotApproved
                || workflow.TestDepotId is null
                || workflow.TestedVersion is null)
            {
                return Result.Failure<(string, string?, string)>(new Error(
                    ErrorCode.InvalidRequest,
                    "Custom package promotion requires an approved test-depot artifact."));
            }

            return Result.Success<(string Mode, string? ArtifactVersion, string Command)>((
                "CUSTOM_PROMOTION",
                workflow.TestedVersion,
                BuildCustomPromotionCommand(productId)));
        }

        Result<bool> validation = ValidateAutomationProfile(productId, profile);
        if (validation.IsFailure)
        {
            return Result.Failure<(string, string?, string)>(validation.Error!);
        }

        ProductVersionSource? source = (await _versionSourceRepository.ListAsync(cancellationToken))
            .FirstOrDefault(item => string.Equals(item.ProductId, productId, StringComparison.OrdinalIgnoreCase));
        if (source?.CheckStatus != "SUCCESS" || string.IsNullOrWhiteSpace(source.LatestVersion))
        {
            return Result.Failure<(string, string?, string)>(new Error(
                ErrorCode.InvalidRequest,
                "A successful manufacturer version check is required before building a custom opsi package."));
        }

        if (!PackageVersionPattern().IsMatch(source.LatestVersion))
        {
            return Result.Failure<(string, string?, string)>(new Error(
                ErrorCode.InvalidRequest,
                "The manufacturer version contains characters that opsi package automation does not support."));
        }

        string remotePath = BuildRemoteArtifactPath(productId, source.LatestVersion);
        return Result.Success<(string Mode, string? ArtifactVersion, string Command)>((
            "CUSTOM_BUILD",
            source.LatestVersion,
            BuildCustomPackageCommand(productId, source.LatestVersion, remotePath, profile)));
    }

    private async Task<Result<RemoteArtifactStageResult>> StagePackageArtifactAsync(
        string productId,
        string artifactVersion,
        PackageUpdateTarget target,
        CancellationToken cancellationToken)
    {
        PackageAutomationProfileOptions profile = _options.PackageAutomationProfiles[productId];
        if (!Uri.TryCreate(profile.ReleaseUrl, UriKind.Absolute, out Uri? releaseUrl))
        {
            return Result.Failure<RemoteArtifactStageResult>(new Error(
                ErrorCode.InvalidRequest,
                "The configured package release URL is invalid."));
        }

        return await _remoteArtifactStager.StageAsync(
            new RemoteArtifactStageRequest(
                releaseUrl,
                profile.ArtifactUrlPattern.Replace(
                    "{version}",
                    Regex.Escape(artifactVersion),
                    StringComparison.Ordinal),
                profile.MaximumArtifactBytes,
                target.Host,
                _options.SshUserName,
                BuildRemoteArtifactPath(productId, artifactVersion),
                _options.SshConnectTimeout,
                _options.PackageTransferTimeout,
                _options.SshIdentityFile),
            cancellationToken);
    }

    internal static Result<bool> ValidateAutomationProfile(
        string productId,
        PackageAutomationProfileOptions profile)
    {
        bool validReleaseUrl = Uri.TryCreate(profile.ReleaseUrl, UriKind.Absolute, out Uri? releaseUrl)
            && releaseUrl.Scheme == Uri.UriSchemeHttps;
        bool validWorkbench = AbsoluteUnixPathPattern().IsMatch(profile.WorkbenchPath)
            && !profile.WorkbenchPath.Split('/').Contains("..", StringComparer.Ordinal);
        bool validInstallerPath = RelativeUnixPathPattern().IsMatch(profile.InstallerRelativePath)
            && !profile.InstallerRelativePath.Split('/').Contains("..", StringComparer.Ordinal);
        bool validPreviousInstallerNames = profile.PreviousInstallerFileNames.All(name =>
            InstallerFileNamePattern().IsMatch(name));
        if (!ProductIdPattern().IsMatch(productId)
            || !validReleaseUrl
            || string.IsNullOrWhiteSpace(profile.ArtifactUrlPattern)
            || !profile.ArtifactUrlPattern.Contains("{version}", StringComparison.Ordinal)
            || !validWorkbench
            || !validInstallerPath
            || !validPreviousInstallerNames
            || profile.MaximumArtifactBytes <= 0)
        {
            return Result.Failure<bool>(new Error(
                ErrorCode.InvalidRequest,
                $"The package automation profile for '{productId}' is incomplete or unsafe."));
        }

        return Result.Success(true);
    }

    internal string BuildCustomPackageCommand(
        string productId,
        string version,
        string remoteArtifactPath,
        PackageAutomationProfileOptions profile)
    {
        string workbench = ShellQuote(profile.WorkbenchPath);
        string buildDirectoryValue = $"/tmp/wec-build-{productId}-{version}";
        string buildDirectory = ShellQuote(buildDirectoryValue);
        string installer = ShellQuote($"{buildDirectoryValue}/{profile.InstallerRelativePath}");
        string staged = ShellQuote(remoteArtifactPath);
        string packagePattern = ShellQuote($"{productId}_{version}-*.opsi");
        string approvedPackage = ShellQuote(BuildApprovedPackagePath(productId));
        string escapedVersion = version.Replace(".", "\\.", StringComparison.Ordinal);
        var commands = new List<string>
        {
            "set -eu",
            $"workbench={workbench}",
            $"builddir={buildDirectory}",
            "control=\"$builddir/OPSI/control.toml\"",
            $"installer={installer}",
            $"staged={staged}",
            "test -d \"$workbench\"",
            "rm -rf -- \"$builddir\"",
            "cp -a -- \"$workbench\" \"$builddir\"",
            "test -f \"$control\"",
            "install -m 0644 \"$staged\" \"$installer\"",
        };
        string stableInstallerName = Path.GetFileName(profile.InstallerRelativePath);
        foreach (string previousInstallerName in profile.PreviousInstallerFileNames)
        {
            string oldPattern = Regex.Escape(previousInstallerName).Replace("\\-", "-", StringComparison.Ordinal);
            commands.Add(
                $"find \"$builddir/CLIENT_DATA\" -type f \\( -name '*.opsiscript' -o -name '*.ins' -o -name '*.opsiinc' \\) -exec sed -i -E {ShellQuote($"s|{oldPattern}|{stableInstallerName}|g")} {{}} +");
        }

        commands.AddRange(
        [
            $"sed -i -E '/^\\[Product\\]/,/^\\[/{'{'}s/^version[[:space:]]*=.*/version = \\\"{version}\\\"/{'}'}' \"$control\"",
            $"grep -Eq '^version[[:space:]]*=[[:space:]]*\"{escapedVersion}\"$' \"$control\"",
            "cd \"$builddir\"",
            $"find . -maxdepth 1 -type f -name {packagePattern} -delete",
            "opsi-makepackage --no-zsync --no-md5",
            $"archive=$(find . -maxdepth 1 -type f -name {packagePattern} -print | sort | tail -n 1)",
            "test -n \"$archive\"",
            $"install -m 0644 \"$archive\" {approvedPackage}",
            $"opsi-package-manager -i {approvedPackage}",
            "cd /",
            "rm -rf -- \"$builddir\"",
            "rm -f -- \"$staged\"",
            $"printf 'WEC_PACKAGE=%s\\n' {approvedPackage}",
        ]);
        string script = string.Join("; ", commands);
        string prefix = _options.UseNonInteractiveSudo ? "sudo -n " : string.Empty;
        return $"{prefix}sh -c {ShellQuote(script)}";
    }

    private string BuildCustomPromotionCommand(string productId)
    {
        string approvedPackage = ShellQuote(BuildApprovedPackagePath(productId));
        string script = string.Join("; ",
            "set -eu",
            $"test -f {approvedPackage}",
            $"opsi-package-manager -i {approvedPackage}");
        string prefix = _options.UseNonInteractiveSudo ? "sudo -n " : string.Empty;
        return $"{prefix}sh -c {ShellQuote(script)}";
    }

    private static string BuildRemoteArtifactPath(string productId, string version) =>
        $"/tmp/wec-{productId}-{version}.artifact";

    private static string BuildApprovedPackagePath(string productId) =>
        $"/tmp/wec-approved-{productId}.opsi";

    private static string ShellQuote(string value) => $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    private static string? FindVersion(
        IReadOnlyList<OpsiProductOnDepot> versions,
        string productId,
        string depotId)
    {
        OpsiProductOnDepot? version = versions.FirstOrDefault(entry =>
            string.Equals(entry.ProductId, productId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.DepotId, depotId, StringComparison.OrdinalIgnoreCase));
        return version is null
            ? null
            : string.IsNullOrEmpty(version.PackageVersion)
                ? version.ProductVersion
                : $"{version.ProductVersion}-{version.PackageVersion}";
    }

    private static string? FormatVersion(string? productVersion, string? packageVersion)
    {
        if (string.IsNullOrWhiteSpace(productVersion))
        {
            return null;
        }

        return string.IsNullOrEmpty(packageVersion)
            ? productVersion
            : $"{productVersion}-{packageVersion}";
    }

    private static string FormatError(Error error) =>
        string.IsNullOrWhiteSpace(error.Details) ? error.Message : $"{error.Message} {error.Details}";

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._+-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProductIdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._+-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageVersionPattern();

    [GeneratedRegex("^/[A-Za-z0-9._/-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AbsoluteUnixPathPattern();

    [GeneratedRegex("^[A-Za-z0-9._/-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex RelativeUnixPathPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._+-]{0,159}$", RegexOptions.CultureInvariant)]
    private static partial Regex InstallerFileNamePattern();
}
