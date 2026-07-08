using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;

namespace Wec.Modules.ActiveDirectory.Application;

public sealed record AdComputer(
    string Name,
    string? DnsHostName,
    string? OperatingSystem,
    bool Enabled,
    string? Description = null);

public sealed record AdComputerSearchResult(
    bool DomainJoined,
    string? DomainName,
    IReadOnlyList<AdComputer> Computers,
    bool Truncated);

/// <summary>
/// LDAP computer search (the Get-ADComputer -Filter equivalent over the
/// existing read-only seam). Feeds the multi-host pickers of the Inventory,
/// Security and Diagnostics pages.
/// </summary>
internal sealed class ComputerSearchService
{
    private readonly DomainContextService _domainContextService;
    private readonly IDirectoryReader _directoryReader;
    private readonly ActiveDirectoryOptions _options;

    public ComputerSearchService(
        DomainContextService domainContextService,
        IDirectoryReader directoryReader,
        IOptions<ActiveDirectoryOptions> options)
    {
        _domainContextService = domainContextService;
        _directoryReader = directoryReader;
        _options = options.Value;
    }

    public async Task<Result<AdComputerSearchResult>> SearchAsync(
        DirectoryConnection connection,
        string? nameFilter,
        bool includeDisabled,
        CancellationToken cancellationToken)
    {
        Result<DomainContext> context =
            await _domainContextService.GetContextAsync(connection, cancellationToken);
        if (context.IsFailure)
        {
            return Result.Failure<AdComputerSearchResult>(context.Error!);
        }

        if (!context.Value.DomainJoined)
        {
            return Result.Success(new AdComputerSearchResult(false, null, [], Truncated: false));
        }

        Result<IReadOnlyList<DirectoryEntryData>> entries = await _directoryReader.SearchAsync(
            new DirectorySearchQuery(
                context.Value.DomainName!,
                context.Value.DefaultNamingContext!,
                AdFilters.ComputersByName(nameFilter, includeDisabled),
                ["name", "dNSHostName", "operatingSystem", "userAccountControl", "description"],
                DirectorySearchScope.Subtree,
                _options.PageSize,
                _options.SearchTimeout,
                connection.Server,
                connection.Credentials),
            cancellationToken);
        if (entries.IsFailure)
        {
            return Result.Failure<AdComputerSearchResult>(entries.Error!);
        }

        List<AdComputer> computers = [.. entries.Value
            .Select(entry => new AdComputer(
                entry.GetFirstValue("name") ?? entry.DistinguishedName,
                entry.GetFirstValue("dNSHostName"),
                entry.GetFirstValue("operatingSystem"),
                Enabled: ((entry.GetLong("userAccountControl") ?? 0) & AdFilters.UacAccountDisabled) == 0,
                entry.GetFirstValue("description")))
            .OrderBy(computer => computer.Name, StringComparer.OrdinalIgnoreCase)];

        bool truncated = computers.Count > _options.ComputerSearchLimit;
        return Result.Success(new AdComputerSearchResult(
            true,
            context.Value.DomainName,
            truncated ? computers[.._options.ComputerSearchLimit] : computers,
            truncated));
    }
}
