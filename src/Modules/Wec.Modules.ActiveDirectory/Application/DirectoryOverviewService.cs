using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.ActiveDirectory.Domain;

namespace Wec.Modules.ActiveDirectory.Application;

internal sealed partial class DirectoryOverviewService
{
    private readonly DomainContextService _domainContextService;
    private readonly IDirectoryReader _directoryReader;
    private readonly IClock _clock;
    private readonly ActiveDirectoryOptions _options;
    private readonly ILogger<DirectoryOverviewService> _logger;

    // DI requires a public constructor even on internal types
    public DirectoryOverviewService(
        DomainContextService domainContextService,
        IDirectoryReader directoryReader,
        IClock clock,
        IOptions<ActiveDirectoryOptions> options,
        ILogger<DirectoryOverviewService> logger)
    {
        _domainContextService = domainContextService;
        _directoryReader = directoryReader;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<AdOverviewResult>> GetOverviewAsync(CancellationToken cancellationToken)
    {
        Result<DomainContext> context = await _domainContextService.GetContextAsync(cancellationToken);
        if (context.IsFailure)
        {
            return Result.Failure<AdOverviewResult>(context.Error!);
        }

        if (!context.Value.DomainJoined)
        {
            // A workgroup machine is a valid answer, not an error (ADR 0006)
            return Result.Success(new AdOverviewResult(
                DomainJoined: false,
                DomainName: null,
                DefaultNamingContext: null,
                DomainControllers: [],
                UserCount: 0,
                DisabledUserCount: 0,
                GroupCount: 0,
                ComputerCount: 0,
                _clock.UtcNow));
        }

        string domainName = context.Value.DomainName!;
        string namingContext = context.Value.DefaultNamingContext!;

        Result<IReadOnlyList<DomainControllerInfo>> domainControllers =
            await DiscoverDomainControllersAsync(domainName, namingContext, cancellationToken);
        if (domainControllers.IsFailure)
        {
            return Result.Failure<AdOverviewResult>(domainControllers.Error!);
        }

        Result<int> userCount = await CountAsync(
            domainName, namingContext, AdFilters.Users, cancellationToken);
        Result<int> disabledUserCount = await CountAsync(
            domainName, namingContext, AdFilters.DisabledUsers, cancellationToken);
        Result<int> groupCount = await CountAsync(
            domainName, namingContext, AdFilters.Groups, cancellationToken);
        Result<int> computerCount = await CountAsync(
            domainName, namingContext, AdFilters.Computers, cancellationToken);

        Result<int>? firstFailedCount = new[] { userCount, disabledUserCount, groupCount, computerCount }
            .FirstOrDefault(count => count.IsFailure);
        if (firstFailedCount is { IsFailure: true })
        {
            return Result.Failure<AdOverviewResult>(firstFailedCount.Error!);
        }

        LogOverviewCaptured(domainName, userCount.Value, groupCount.Value, domainControllers.Value.Count);
        return Result.Success(new AdOverviewResult(
            DomainJoined: true,
            domainName,
            namingContext,
            domainControllers.Value,
            userCount.Value,
            disabledUserCount.Value,
            groupCount.Value,
            computerCount.Value,
            _clock.UtcNow));
    }

    private async Task<Result<IReadOnlyList<DomainControllerInfo>>> DiscoverDomainControllersAsync(
        string domainName,
        string namingContext,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<DirectoryEntryData>> entries = await _directoryReader.SearchAsync(
            BuildQuery(domainName, namingContext, AdFilters.DomainControllers, ["dNSHostName"]),
            cancellationToken);
        if (entries.IsFailure)
        {
            return Result.Failure<IReadOnlyList<DomainControllerInfo>>(entries.Error!);
        }

        return Result.Success<IReadOnlyList<DomainControllerInfo>>(
        [
            .. entries.Value.Select(entry => new DomainControllerInfo(
                entry.GetFirstValue("dNSHostName") ?? entry.DistinguishedName,
                entry.DistinguishedName)),
        ]);
    }

    private async Task<Result<int>> CountAsync(
        string domainName,
        string namingContext,
        string ldapFilter,
        CancellationToken cancellationToken)
    {
        // Empty attribute list = entries only, no attribute payload
        Result<IReadOnlyList<DirectoryEntryData>> entries = await _directoryReader.SearchAsync(
            BuildQuery(domainName, namingContext, ldapFilter, []),
            cancellationToken);
        return entries.IsFailure
            ? Result.Failure<int>(entries.Error!)
            : Result.Success(entries.Value.Count);
    }

    private DirectorySearchQuery BuildQuery(
        string domainName,
        string baseDistinguishedName,
        string ldapFilter,
        IReadOnlyList<string> attributes) =>
        new(
            domainName,
            baseDistinguishedName,
            ldapFilter,
            attributes,
            DirectorySearchScope.Subtree,
            _options.PageSize,
            _options.SearchTimeout);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "AD overview captured for {DomainName}: {UserCount} users, {GroupCount} groups, {DomainControllerCount} DCs")]
    private partial void LogOverviewCaptured(string domainName, int userCount, int groupCount, int domainControllerCount);
}
