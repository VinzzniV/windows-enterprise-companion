using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.ActiveDirectory.Application;
using Wec.Modules.ActiveDirectory.Domain;

namespace Wec.Modules.ActiveDirectory.Handlers;

public sealed record GetAdHygieneRulePageRequest(
    string RuleId,
    DateTimeOffset EvaluatedAtUtc,
    string? Query = null,
    int Page = 1,
    int PageSize = 50,
    DirectoryConnectionRequest? Connection = null);

internal sealed class GetAdHygieneRulePageHandler
    : IActionHandler<GetAdHygieneRulePageRequest, AdHygieneRulePage>
{
    private readonly DirectoryHygieneService _directoryHygieneService;

    public GetAdHygieneRulePageHandler(DirectoryHygieneService directoryHygieneService)
    {
        _directoryHygieneService = directoryHygieneService;
    }

    public string Module => "activedirectory";

    public string Action => "getHygieneRulePage";

    public Task<Result<AdHygieneRulePage>> HandleAsync(
        GetAdHygieneRulePageRequest payload,
        CancellationToken cancellationToken)
    {
        Result<DirectoryConnection> connection =
            (payload.Connection ?? new DirectoryConnectionRequest()).ToConnection();
        if (connection.IsFailure)
        {
            return Task.FromResult(Result.Failure<AdHygieneRulePage>(connection.Error!));
        }

        return _directoryHygieneService.GetRulePageAsync(
            connection.Value,
            payload.RuleId,
            payload.EvaluatedAtUtc,
            payload.Query,
            payload.Page,
            payload.PageSize,
            cancellationToken);
    }
}
