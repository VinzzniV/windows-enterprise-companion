using Microsoft.Extensions.Logging;
using Wec.Core.Messaging;
using Wec.Core.Printing;
using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Modules.PrintManagement.Handlers;

/// <summary>
/// Deletes unused printer ports the user picked from the scan result. The one
/// deliberate write in this module: it demands an explicit confirmation, runs as
/// the signed-in admin against the server, and audits every attempt to the log.
/// </summary>
public sealed record DeleteUnusedPortsRequest(
    TargetRequest Target, IReadOnlyList<string> PortNames, bool Confirmed);

public sealed record DeleteUnusedPortsResult(IReadOnlyList<PortRemovalResult> Results);

internal sealed partial class DeleteUnusedPortsHandler
    : IActionHandler<DeleteUnusedPortsRequest, DeleteUnusedPortsResult>
{
    private readonly IPrinterPortRemover _portRemover;
    private readonly ILogger<DeleteUnusedPortsHandler> _logger;

    public DeleteUnusedPortsHandler(
        IPrinterPortRemover portRemover, ILogger<DeleteUnusedPortsHandler> logger)
    {
        _portRemover = portRemover;
        _logger = logger;
    }

    public string Module => "printmanagement";

    public string Action => "deleteUnusedPorts";

    public async Task<Result<DeleteUnusedPortsResult>> HandleAsync(
        DeleteUnusedPortsRequest payload, CancellationToken cancellationToken)
    {
        if (!payload.Confirmed)
        {
            return Result.Failure<DeleteUnusedPortsResult>(new Error(
                ErrorCode.InvalidRequest,
                "Deleting printer ports must be explicitly confirmed after reviewing the list."));
        }

        TargetRequest target = payload.Target ?? new TargetRequest();
        if (string.IsNullOrWhiteSpace(target.Host))
        {
            return Result.Failure<DeleteUnusedPortsResult>(new Error(
                ErrorCode.InvalidRequest, "A print server is required to delete ports."));
        }

        string[] portNames = [.. (payload.PortNames ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        if (portNames.Length == 0)
        {
            return Result.Failure<DeleteUnusedPortsResult>(new Error(
                ErrorCode.InvalidRequest, "Select at least one port to delete."));
        }

        Result<ScanCredentials> credentials = target.ToScanCredentials();
        if (credentials.IsFailure)
        {
            return Result.Failure<DeleteUnusedPortsResult>(credentials.Error!);
        }

        string server = target.Host!.Trim();
        string actor = Environment.UserName;
        LogRequested(actor, server, portNames);

        Result<IReadOnlyList<PortRemovalResult>> removal =
            await _portRemover.RemovePortsAsync(server, portNames, credentials.Value, cancellationToken);
        if (removal.IsFailure)
        {
            LogFailed(server, actor, removal.Error!.Message);
            return Result.Failure<DeleteUnusedPortsResult>(removal.Error!);
        }

        string[] removed = [.. removal.Value.Where(port => port.Removed).Select(port => port.Name)];
        string[] refused = [.. removal.Value
            .Where(port => !port.Removed)
            .Select(port => $"{port.Name} ({port.Error})")];
        LogCompleted(actor, server, removed, refused);

        return Result.Success(new DeleteUnusedPortsResult(removal.Value));
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Printer port deletion requested by {Actor} on {Server}: {Ports}")]
    private partial void LogRequested(string actor, string server, string[] ports);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Printer port deletion by {Actor} on {Server} — removed: {Removed}; refused: {Refused}")]
    private partial void LogCompleted(string actor, string server, string[] removed, string[] refused);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Printer port deletion on {Server} failed for {Actor}: {Error}")]
    private partial void LogFailed(string server, string actor, string error);
}
