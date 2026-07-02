using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;

namespace Wec.Modules.ActiveDirectory.Application;

internal sealed record DomainContext(bool DomainJoined, string? DomainName, string? DefaultNamingContext);

/// <summary>
/// Shared first step of every AD analysis: local domain detection via WMI
/// (a workgroup machine never causes a directory connection, ADR 0006),
/// then the default naming context from the RootDSE.
/// </summary>
internal sealed class DomainContextService
{
    private const string CimV2Namespace = @"root\cimv2";

    private readonly IWmiQueryService _wmiQueryService;
    private readonly IDirectoryReader _directoryReader;
    private readonly ActiveDirectoryOptions _options;

    public DomainContextService(
        IWmiQueryService wmiQueryService,
        IDirectoryReader directoryReader,
        IOptions<ActiveDirectoryOptions> options)
    {
        _wmiQueryService = wmiQueryService;
        _directoryReader = directoryReader;
        _options = options.Value;
    }

    public async Task<Result<DomainContext>> GetContextAsync(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<WmiInstance>> computerSystems = await _wmiQueryService.QueryAsync(
            CimV2Namespace,
            "SELECT PartOfDomain, Domain FROM Win32_ComputerSystem",
            cancellationToken);
        if (computerSystems.IsFailure)
        {
            return Result.Failure<DomainContext>(computerSystems.Error!);
        }

        WmiInstance? computerSystem = computerSystems.Value.Count > 0 ? computerSystems.Value[0] : null;
        bool partOfDomain = computerSystem?.GetValue<bool?>("PartOfDomain") ?? false;
        if (computerSystem is null || !partOfDomain)
        {
            return Result.Success(new DomainContext(false, null, null));
        }

        string domainName = computerSystem.GetString("Domain")!;
        Result<IReadOnlyList<DirectoryEntryData>> rootDse = await _directoryReader.SearchAsync(
            new DirectorySearchQuery(
                domainName,
                string.Empty,
                "(objectClass=*)",
                ["defaultNamingContext"],
                DirectorySearchScope.Base,
                _options.PageSize,
                _options.SearchTimeout),
            cancellationToken);
        if (rootDse.IsFailure)
        {
            return Result.Failure<DomainContext>(rootDse.Error!);
        }

        string? namingContext = rootDse.Value.Count > 0
            ? rootDse.Value[0].GetFirstValue("defaultNamingContext")
            : null;
        return namingContext is not null
            ? Result.Success(new DomainContext(true, domainName, namingContext))
            : Result.Failure<DomainContext>(new Error(
                ErrorCode.DirectoryUnavailable,
                "The RootDSE did not expose a default naming context."));
    }
}
