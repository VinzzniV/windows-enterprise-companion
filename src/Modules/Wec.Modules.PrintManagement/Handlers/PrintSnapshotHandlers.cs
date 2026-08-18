using Microsoft.Extensions.Options;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.PrintManagement.Application;
using Wec.Modules.PrintManagement.Domain;
using Wec.Modules.PrintManagement.Persistence;

namespace Wec.Modules.PrintManagement.Handlers;

public sealed record ListPrintServersRequest;

public sealed record ListPrintServersResult(IReadOnlyList<StoredPrintServer> Servers);

internal sealed class ListPrintServersHandler : IActionHandler<ListPrintServersRequest, ListPrintServersResult>
{
    private readonly IPrintSnapshotRepository _repository;

    public ListPrintServersHandler(IPrintSnapshotRepository repository)
    {
        _repository = repository;
    }

    public string Module => "printmanagement";

    public string Action => "listServers";

    public async Task<Result<ListPrintServersResult>> HandleAsync(
        ListPrintServersRequest payload, CancellationToken cancellationToken) =>
        Result.Success(new ListPrintServersResult(await _repository.ListServersAsync(cancellationToken)));
}

public sealed record GetLatestPrintSnapshotRequest(string Server);

internal sealed class GetLatestPrintSnapshotHandler
    : IActionHandler<GetLatestPrintSnapshotRequest, PrintServerSnapshot>
{
    private readonly IPrintSnapshotRepository _repository;
    private readonly LastKnownDevices _lastKnownDevices;

    public GetLatestPrintSnapshotHandler(
        IPrintSnapshotRepository repository, LastKnownDevices lastKnownDevices)
    {
        _repository = repository;
        _lastKnownDevices = lastKnownDevices;
    }

    public string Module => "printmanagement";

    public string Action => "getLatest";

    public async Task<Result<PrintServerSnapshot>> HandleAsync(
        GetLatestPrintSnapshotRequest payload, CancellationToken cancellationToken)
    {
        PrintServerSnapshot? snapshot = await _repository.GetLatestAsync(
            payload.Server.ToUpperInvariant(), cancellationToken);
        return snapshot is null
            ? Result.Failure<PrintServerSnapshot>(Error.NotFound(
                $"No stored snapshot for '{payload.Server}'."))
            : Result.Success(await _lastKnownDevices.FillAsync(snapshot, cancellationToken));
    }
}

public sealed record GetPrintHistoryRequest(string Server);

public sealed record PrintHistoryResult(IReadOnlyList<PrintSnapshotStamp> Snapshots);

internal sealed class GetPrintHistoryHandler : IActionHandler<GetPrintHistoryRequest, PrintHistoryResult>
{
    private readonly IPrintSnapshotRepository _repository;

    public GetPrintHistoryHandler(IPrintSnapshotRepository repository)
    {
        _repository = repository;
    }

    public string Module => "printmanagement";

    public string Action => "getHistory";

    public async Task<Result<PrintHistoryResult>> HandleAsync(
        GetPrintHistoryRequest payload, CancellationToken cancellationToken) =>
        Result.Success(new PrintHistoryResult(await _repository.GetHistoryAsync(
            payload.Server.ToUpperInvariant(), cancellationToken)));
}

public sealed record GetLeaseDiffRequest(string Server, long? BaselineSnapshotId = null);

internal sealed class GetLeaseDiffHandler : IActionHandler<GetLeaseDiffRequest, PrintServerDiff>
{
    private readonly IPrintSnapshotRepository _repository;

    public GetLeaseDiffHandler(IPrintSnapshotRepository repository)
    {
        _repository = repository;
    }

    public string Module => "printmanagement";

    public string Action => "getDiff";

    public async Task<Result<PrintServerDiff>> HandleAsync(
        GetLeaseDiffRequest payload, CancellationToken cancellationToken)
    {
        string server = payload.Server.ToUpperInvariant();
        PrintServerSnapshot? latest = await _repository.GetLatestAsync(server, cancellationToken);
        if (latest is null)
        {
            return Result.Failure<PrintServerDiff>(Error.NotFound($"No stored snapshot for '{payload.Server}'."));
        }

        PrintServerSnapshot? baseline;
        if (payload.BaselineSnapshotId is { } baselineId)
        {
            baseline = await _repository.GetByIdAsync(baselineId, cancellationToken);
        }
        else
        {
            IReadOnlyList<PrintSnapshotStamp> history =
                await _repository.GetHistoryAsync(server, cancellationToken);
            baseline = history.Count > 1
                ? await _repository.GetByIdAsync(history[1].Id, cancellationToken)
                : null;
        }

        if (baseline is null)
        {
            return Result.Failure<PrintServerDiff>(Error.NotFound(
                $"No baseline snapshot for '{payload.Server}' — the diff needs at least two scans."));
        }

        return Result.Success(LeaseDiff.Compute(baseline, latest));
    }
}

public sealed record DeletePrintServerRequest(string Server);

public sealed record DeletePrintServerResult(string Server);

internal sealed class DeletePrintServerHandler
    : IActionHandler<DeletePrintServerRequest, DeletePrintServerResult>
{
    private readonly IPrintSnapshotRepository _repository;

    public DeletePrintServerHandler(IPrintSnapshotRepository repository)
    {
        _repository = repository;
    }

    public string Module => "printmanagement";

    public string Action => "deleteServer";

    public async Task<Result<DeletePrintServerResult>> HandleAsync(
        DeletePrintServerRequest payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload.Server))
        {
            return Result.Failure<DeletePrintServerResult>(new Error(
                ErrorCode.InvalidRequest, "The server name is required."));
        }

        string server = payload.Server.ToUpperInvariant();
        await _repository.DeleteServerAsync(server, cancellationToken);
        return Result.Success(new DeletePrintServerResult(server));
    }
}

public sealed record GetPrintHintsRequest;

public sealed record PrintHintsResult(IReadOnlyList<PrintHint> Hints);

internal sealed class GetPrintHintsHandler : IActionHandler<GetPrintHintsRequest, PrintHintsResult>
{
    private readonly IPrintSnapshotRepository _repository;
    private readonly PrintManagementOptions _options;

    public GetPrintHintsHandler(IPrintSnapshotRepository repository, IOptions<PrintManagementOptions> options)
    {
        _repository = repository;
        _options = options.Value;
    }

    public string Module => "printmanagement";

    public string Action => "getHints";

    public async Task<Result<PrintHintsResult>> HandleAsync(
        GetPrintHintsRequest payload, CancellationToken cancellationToken)
    {
        var snapshots = new List<PrintServerSnapshot>();
        foreach (StoredPrintServer server in await _repository.ListServersAsync(cancellationToken))
        {
            if (await _repository.GetLatestAsync(server.Server, cancellationToken) is { } snapshot)
            {
                snapshots.Add(snapshot);
            }
        }

        return Result.Success(new PrintHintsResult(
            PrintConsistency.ComputeHints(snapshots, _options.SnmpCommunity)));
    }
}
