using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;

namespace Wec.Modules.ActiveDirectory.Application;

public sealed record AdComputer(
    string Name,
    string? DnsHostName,
    string? OperatingSystem,
    bool Enabled,
    string? Description = null,
    string? DistinguishedName = null,
    DateTimeOffset? LastLogonDate = null);

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
internal sealed class ComputerSearchService : IAdComputerInventoryProvider
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
        CancellationToken cancellationToken,
        int? resultLimit = null)
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

        int limit = resultLimit ?? _options.ComputerSearchLimit;
        Result<BoundedDirectorySearchResult> entries = await _directoryReader.SearchBoundedAsync(
            new DirectorySearchQuery(
                context.Value.DomainName!,
                context.Value.DefaultNamingContext!,
                AdFilters.ComputersByName(nameFilter, includeDisabled),
                ["name", "dNSHostName", "operatingSystem", "userAccountControl", "description", "lastLogonTimestamp"],
                DirectorySearchScope.Subtree,
                _options.PageSize,
                _options.SearchTimeout,
                connection.Server,
                connection.Credentials),
            limit,
            cancellationToken);
        if (entries.IsFailure)
        {
            return Result.Failure<AdComputerSearchResult>(entries.Error!);
        }

        List<AdComputer> computers = [.. entries.Value.Entries
            .Select(entry => new AdComputer(
                entry.GetFirstValue("name") ?? entry.DistinguishedName,
                entry.GetFirstValue("dNSHostName"),
                entry.GetFirstValue("operatingSystem"),
                Enabled: ((entry.GetLong("userAccountControl") ?? 0) & AdFilters.UacAccountDisabled) == 0,
                entry.GetFirstValue("description"),
                entry.DistinguishedName,
                ParseFileTime(entry.GetLong("lastLogonTimestamp"))))
            .OrderBy(computer => computer.Name, StringComparer.OrdinalIgnoreCase)];

        bool truncated = entries.Value.TotalCount > computers.Count;
        return Result.Success(new AdComputerSearchResult(
            true,
            context.Value.DomainName,
            computers,
            truncated));
    }

    public async Task<Result<AdComputerInventory>> LoadAsync(
        AdComputerInventoryQuery query,
        CancellationToken cancellationToken)
    {
        var connection = new DirectoryConnection(query.Domain, query.Server, query.Credentials);
        Result<AdComputerSearchResult> result = await SearchAsync(
            connection,
            nameFilter: null,
            includeDisabled: true,
            cancellationToken,
            resultLimit: Math.Max(1, query.Limit));
        if (result.IsFailure)
        {
            return Result.Failure<AdComputerInventory>(result.Error!);
        }

        int limit = Math.Max(1, query.Limit);
        IReadOnlyList<AdComputer> source = result.Value.Computers;
        bool truncated = result.Value.Truncated || source.Count > limit;
        IReadOnlyList<AdComputerInventoryItem> computers = source
            .Take(limit)
            .Select(computer => new AdComputerInventoryItem(
                computer.Name,
                computer.DnsHostName,
                computer.OperatingSystem,
                computer.Description,
                computer.Enabled,
                computer.DistinguishedName ?? string.Empty,
                computer.LastLogonDate))
            .ToList();

        return Result.Success(new AdComputerInventory(
            result.Value.DomainJoined,
            result.Value.DomainName,
            computers,
            truncated));
    }

    private static DateTimeOffset? ParseFileTime(long? fileTime)
    {
        if (fileTime is null or <= 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromFileTime(fileTime.Value).ToUniversalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
