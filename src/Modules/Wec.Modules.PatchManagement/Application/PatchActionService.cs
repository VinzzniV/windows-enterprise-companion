using System.Text.Json;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Opsi;
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

public sealed record PreparePackagesPlan(string Command, string Note);

/// <summary>
/// Every action goes preview → confirm → execute and always leaves an audit
/// entry — including failed and merely planned ones (ADR 0008).
/// </summary>
public sealed class PatchActionService
{
    internal const string RolloutAction = "ROLLOUT_REQUESTED";
    internal const string PreparePackagesAction = "PACKAGE_UPDATE_PLANNED";

    private readonly IOpsiClient _opsiClient;
    private readonly OpsiSessionState _sessionState;
    private readonly PatchDashboardService _dashboardService;
    private readonly IPatchAuditRepository _auditRepository;
    private readonly IClock _clock;

    public PatchActionService(
        IOpsiClient opsiClient,
        OpsiSessionState sessionState,
        PatchDashboardService dashboardService,
        IPatchAuditRepository auditRepository,
        IClock clock)
    {
        _opsiClient = opsiClient;
        _sessionState = sessionState;
        _dashboardService = dashboardService;
        _auditRepository = auditRepository;
        _clock = clock;
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

    public async Task<Result<PreparePackagesPlan>> PlanPackageUpdateAsync(
        IReadOnlyList<string> productIds, CancellationToken cancellationToken)
    {
        if (_sessionState.Current is not { } session)
        {
            return Result.Failure<PreparePackagesPlan>(PatchDashboardService.NotConnected);
        }

        if (productIds.Count == 0)
        {
            return Result.Failure<PreparePackagesPlan>(new Error(
                ErrorCode.InvalidRequest, "Select at least one product to prepare."));
        }

        // JSON-RPC cannot run opsi-package-updater; the planned command is the
        // deliberate MVP boundary (ADR 0008) — executing it over SSH is a
        // documented follow-up
        var plan = new PreparePackagesPlan(
            $"opsi-package-updater -v update {string.Join(' ', productIds.Order(StringComparer.OrdinalIgnoreCase))}",
            $"Run this on the opsi server '{session.Connection.ServiceUrl.Host}' over SSH; "
            + "WEC records the intent in the audit log but does not execute it.");

        await _auditRepository.AddAsync(
            new PatchAuditEntry(
                Id: 0,
                _clock.UtcNow,
                Environment.UserName,
                PreparePackagesAction,
                string.Join(", ", productIds),
                DepotId: null,
                TargetClients: [],
                JsonSerializer.Serialize(plan),
                "PLANNED",
                ErrorMessage: null),
            cancellationToken);

        return Result.Success(plan);
    }
}
