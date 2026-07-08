using Microsoft.Extensions.Options;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Diagnostics.Application;

namespace Wec.Modules.Diagnostics.Handlers;

public sealed record QueryEventLogRequest(string Preset, TargetRequest? Target = null);

/// <summary>
/// Live event-log query (canned presets) against the local machine or a
/// remote target with the session credentials. Results are not persisted —
/// this is a troubleshooting view, not an inventory.
/// </summary>
internal sealed class QueryEventLogHandler : IActionHandler<QueryEventLogRequest, EventLogQueryResult>
{
    private readonly EventLogQueryService _eventLogQueryService;
    private readonly RemoteScanOptions _remoteScanOptions;

    public QueryEventLogHandler(
        EventLogQueryService eventLogQueryService,
        IOptions<RemoteScanOptions> remoteScanOptions)
    {
        _eventLogQueryService = eventLogQueryService;
        _remoteScanOptions = remoteScanOptions.Value;
    }

    public string Module => "diagnostics";

    public string Action => "queryEventLog";

    public Task<Result<EventLogQueryResult>> HandleAsync(
        QueryEventLogRequest payload,
        CancellationToken cancellationToken)
    {
        TargetRequest targetRequest = payload.Target ?? new TargetRequest();
        Result<ScanCredentials> credentials = targetRequest.ToScanCredentials();
        if (credentials.IsFailure)
        {
            return Task.FromResult(Result.Failure<EventLogQueryResult>(credentials.Error!));
        }

        var context = new DiagnosticContext(
            targetRequest.ToScanTarget(),
            credentials.Value,
            _remoteScanOptions.ToConnectionOptions());
        return _eventLogQueryService.QueryAsync(context, payload.Preset, cancellationToken);
    }
}
