using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Inventory.Application;
using Wec.Modules.Inventory.Domain;

namespace Wec.Modules.Inventory.Handlers;

public sealed record RunBatchInventoryRequest(
    IReadOnlyList<string>? Hosts = null,
    string? UserName = null,
    string? Domain = null,
    string? Password = null);

internal sealed class RunBatchInventoryHandler
    : IActionHandler<RunBatchInventoryRequest, InventoryBatchResult>
{
    private readonly BatchInventoryService _batchInventoryService;

    public RunBatchInventoryHandler(BatchInventoryService batchInventoryService)
    {
        _batchInventoryService = batchInventoryService;
    }

    public string Module => "inventory";

    public string Action => "runBatchScan";

    public Task<Result<InventoryBatchResult>> HandleAsync(
        RunBatchInventoryRequest payload,
        CancellationToken cancellationToken)
    {
        ScanCredentials credentials;
        if (string.IsNullOrWhiteSpace(payload.UserName))
        {
            credentials = ScanCredentials.CurrentUser;
        }
        else if (payload.Password is null)
        {
            return Task.FromResult(Result.Failure<InventoryBatchResult>(new Error(
                ErrorCode.InvalidRequest,
                "Explicit credentials require a password.")));
        }
        else
        {
            credentials = ScanCredentials.Explicit(payload.UserName, payload.Domain, payload.Password);
        }

        return _batchInventoryService.RunAsync(
            payload.Hosts ?? [],
            credentials,
            cancellationToken);
    }
}
