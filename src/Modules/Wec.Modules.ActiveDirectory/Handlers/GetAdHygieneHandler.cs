using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.ActiveDirectory.Application;
using Wec.Modules.ActiveDirectory.Domain;

namespace Wec.Modules.ActiveDirectory.Handlers;

public sealed record GetAdHygieneRequest(DirectoryConnectionRequest? Connection = null);

internal sealed class GetAdHygieneHandler : IActionHandler<GetAdHygieneRequest, AdHygieneResult>
{
    private readonly DirectoryHygieneService _directoryHygieneService;

    public GetAdHygieneHandler(DirectoryHygieneService directoryHygieneService)
    {
        _directoryHygieneService = directoryHygieneService;
    }

    public string Module => "activedirectory";

    public string Action => "getHygiene";

    public Task<Result<AdHygieneResult>> HandleAsync(
        GetAdHygieneRequest payload,
        CancellationToken cancellationToken)
    {
        Result<DirectoryConnection> connection =
            (payload.Connection ?? new DirectoryConnectionRequest()).ToConnection();
        if (connection.IsFailure)
        {
            return Task.FromResult(Result.Failure<AdHygieneResult>(connection.Error!));
        }

        return _directoryHygieneService.GetHygieneAsync(connection.Value, cancellationToken);
    }
}
