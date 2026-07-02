using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.ActiveDirectory.Domain;

namespace Wec.Modules.ActiveDirectory.Application;

public sealed partial class DirectoryOverviewService
{
    private const string CimV2Namespace = @"root\cimv2";

    // userAccountControl bitwise-AND matching rule (LDAP_MATCHING_RULE_BIT_AND)
    private const string UacBitFilter = "userAccountControl:1.2.840.113556.1.4.803:=";
    private const int UacAccountDisabled = 2;
    private const int UacServerTrustAccount = 8192;

    private readonly IWmiQueryService _wmiQueryService;
    private readonly IDirectoryReader _directoryReader;
    private readonly IClock _clock;
    private readonly ActiveDirectoryOptions _options;
    private readonly ILogger<DirectoryOverviewService> _logger;

    public DirectoryOverviewService(
        IWmiQueryService wmiQueryService,
        IDirectoryReader directoryReader,
        IClock clock,
        IOptions<ActiveDirectoryOptions> options,
        ILogger<DirectoryOverviewService> logger)
    {
        _wmiQueryService = wmiQueryService;
        _directoryReader = directoryReader;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<AdOverviewResult>> GetOverviewAsync(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<WmiInstance>> computerSystems = await _wmiQueryService.QueryAsync(
            CimV2Namespace,
            "SELECT PartOfDomain, Domain FROM Win32_ComputerSystem",
            cancellationToken);
        if (computerSystems.IsFailure)
        {
            return Result.Failure<AdOverviewResult>(computerSystems.Error!);
        }

        WmiInstance? computerSystem = computerSystems.Value.Count > 0 ? computerSystems.Value[0] : null;
        bool partOfDomain = computerSystem?.GetValue<bool?>("PartOfDomain") ?? false;
        if (computerSystem is null || !partOfDomain)
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

        string domainName = computerSystem.GetString("Domain")!;

        Result<string> namingContext = await ReadDefaultNamingContextAsync(domainName, cancellationToken);
        if (namingContext.IsFailure)
        {
            return Result.Failure<AdOverviewResult>(namingContext.Error!);
        }

        Result<IReadOnlyList<DomainControllerInfo>> domainControllers =
            await DiscoverDomainControllersAsync(domainName, namingContext.Value, cancellationToken);
        if (domainControllers.IsFailure)
        {
            return Result.Failure<AdOverviewResult>(domainControllers.Error!);
        }

        Result<int> userCount = await CountAsync(
            domainName, namingContext.Value, "(&(objectCategory=person)(objectClass=user))", cancellationToken);
        Result<int> disabledUserCount = await CountAsync(
            domainName,
            namingContext.Value,
            $"(&(objectCategory=person)(objectClass=user)({UacBitFilter}{UacAccountDisabled}))",
            cancellationToken);
        Result<int> groupCount = await CountAsync(
            domainName, namingContext.Value, "(objectCategory=group)", cancellationToken);
        Result<int> computerCount = await CountAsync(
            domainName, namingContext.Value, "(objectCategory=computer)", cancellationToken);

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
            namingContext.Value,
            domainControllers.Value,
            userCount.Value,
            disabledUserCount.Value,
            groupCount.Value,
            computerCount.Value,
            _clock.UtcNow));
    }

    private async Task<Result<string>> ReadDefaultNamingContextAsync(
        string domainName,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<DirectoryEntryData>> rootDse = await _directoryReader.SearchAsync(
            BuildQuery(domainName, string.Empty, "(objectClass=*)", ["defaultNamingContext"], DirectorySearchScope.Base),
            cancellationToken);
        if (rootDse.IsFailure)
        {
            return Result.Failure<string>(rootDse.Error!);
        }

        string? namingContext = rootDse.Value.Count > 0
            ? rootDse.Value[0].GetFirstValue("defaultNamingContext")
            : null;
        return namingContext is not null
            ? Result.Success(namingContext)
            : Result.Failure<string>(new Error(
                ErrorCode.DirectoryUnavailable,
                "The RootDSE did not expose a default naming context."));
    }

    private async Task<Result<IReadOnlyList<DomainControllerInfo>>> DiscoverDomainControllersAsync(
        string domainName,
        string namingContext,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<DirectoryEntryData>> entries = await _directoryReader.SearchAsync(
            BuildQuery(
                domainName,
                namingContext,
                $"(&(objectCategory=computer)({UacBitFilter}{UacServerTrustAccount}))",
                ["dNSHostName"],
                DirectorySearchScope.Subtree),
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
            BuildQuery(domainName, namingContext, ldapFilter, [], DirectorySearchScope.Subtree),
            cancellationToken);
        return entries.IsFailure
            ? Result.Failure<int>(entries.Error!)
            : Result.Success(entries.Value.Count);
    }

    private DirectorySearchQuery BuildQuery(
        string domainName,
        string baseDistinguishedName,
        string ldapFilter,
        IReadOnlyList<string> attributes,
        DirectorySearchScope scope) =>
        new(domainName, baseDistinguishedName, ldapFilter, attributes, scope, _options.PageSize, _options.SearchTimeout);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "AD overview captured for {DomainName}: {UserCount} users, {GroupCount} groups, {DomainControllerCount} DCs")]
    private partial void LogOverviewCaptured(string domainName, int userCount, int groupCount, int domainControllerCount);
}
