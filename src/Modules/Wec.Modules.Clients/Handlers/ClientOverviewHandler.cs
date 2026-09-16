using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.Clients.Application;

namespace Wec.Modules.Clients.Handlers;

public sealed record GetClientOverviewRequest(string Host);

internal sealed class GetClientOverviewHandler : IActionHandler<GetClientOverviewRequest, ClientOverviewResult>
{
    private readonly ClientOverviewService _service;

    public GetClientOverviewHandler(ClientOverviewService service)
    {
        _service = service;
    }

    public string Module => "clients";

    public string Action => "getOverview";

    public async Task<Result<ClientOverviewResult>> HandleAsync(
        GetClientOverviewRequest payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload.Host))
        {
            return Result.Failure<ClientOverviewResult>(new Error(
                ErrorCode.InvalidRequest,
                "A client host is required."));
        }

        return Result.Success(await _service.GetAsync(payload.Host.Trim(), cancellationToken));
    }
}
