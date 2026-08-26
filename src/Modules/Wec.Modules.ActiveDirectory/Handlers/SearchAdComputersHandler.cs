using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.ActiveDirectory.Application;

namespace Wec.Modules.ActiveDirectory.Handlers;

public sealed record SearchAdComputersRequest(
    string? NameFilter = null,
    bool IncludeDisabled = false,
    DirectoryConnectionRequest? Connection = null,
    int? ResultLimit = null);

/// <summary>
/// Computer discovery for the multi-host scan pickers: filter AD computers
/// (substring or '*' wildcard on name/dNSHostName, enabled-only by default)
/// and feed the result into an Inventory/Security/Diagnostics run.
/// </summary>
internal sealed class SearchAdComputersHandler
    : IActionHandler<SearchAdComputersRequest, AdComputerSearchResult>
{
    private readonly ComputerSearchService _computerSearchService;

    public SearchAdComputersHandler(ComputerSearchService computerSearchService)
    {
        _computerSearchService = computerSearchService;
    }

    public string Module => "activedirectory";

    public string Action => "searchComputers";

    public async Task<Result<AdComputerSearchResult>> HandleAsync(
        SearchAdComputersRequest payload,
        CancellationToken cancellationToken)
    {
        if (payload.ResultLimit is < 1 or > 100)
        {
            return Result.Failure<AdComputerSearchResult>(new Error(
                ErrorCode.InvalidRequest,
                "resultLimit must be between 1 and 100 when supplied."));
        }

        Result<DirectoryConnection> connection =
            (payload.Connection ?? new DirectoryConnectionRequest()).ToConnection();
        if (connection.IsFailure)
        {
            return Result.Failure<AdComputerSearchResult>(connection.Error!);
        }

        return await _computerSearchService.SearchAsync(
            connection.Value,
            payload.NameFilter,
            payload.IncludeDisabled,
            cancellationToken,
            payload.ResultLimit);
    }
}
