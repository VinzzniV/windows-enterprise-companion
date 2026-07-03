using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.ActiveDirectory.Application;
using Wec.Modules.ActiveDirectory.Domain;

namespace Wec.Modules.ActiveDirectory.Handlers;

public sealed record GetAdOverviewRequest(DirectoryConnectionRequest? Connection = null);

internal sealed class GetAdOverviewHandler : IActionHandler<GetAdOverviewRequest, AdOverviewResult>
{
    private readonly DirectoryOverviewService _directoryOverviewService;

    public GetAdOverviewHandler(DirectoryOverviewService directoryOverviewService)
    {
        _directoryOverviewService = directoryOverviewService;
    }

    public string Module => "activedirectory";

    public string Action => "getOverview";

    public Task<Result<AdOverviewResult>> HandleAsync(
        GetAdOverviewRequest payload,
        CancellationToken cancellationToken)
    {
        Result<DirectoryConnection> connection =
            (payload.Connection ?? new DirectoryConnectionRequest()).ToConnection();
        if (connection.IsFailure)
        {
            return Task.FromResult(Result.Failure<AdOverviewResult>(connection.Error!));
        }

        return _directoryOverviewService.GetOverviewAsync(connection.Value, cancellationToken);
    }
}
