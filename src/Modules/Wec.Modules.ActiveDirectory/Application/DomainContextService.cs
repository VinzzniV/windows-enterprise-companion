using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;

namespace Wec.Modules.ActiveDirectory.Application;

internal sealed record DomainContext(bool DomainJoined, string? DomainName, string? DefaultNamingContext);

/// <summary>
/// Shared first step of every AD analysis: local domain detection via WMI
/// (a workgroup machine never causes a directory connection, ADR 0006),
/// then the default naming context from the RootDSE. An explicit domain in
/// the connection skips the local detection — that is how a workgroup
/// machine analyzes a domain it is not joined to (ADR 0006 revision).
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

    public async Task<Result<DomainContext>> GetContextAsync(
        DirectoryConnection connection,
        CancellationToken cancellationToken)
    {
        string domainName;
        if (connection.DomainOverride is not null)
        {
            domainName = connection.DomainOverride;
        }
        else
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

            domainName = computerSystem.GetString("Domain")!;
        }

        Result<IReadOnlyList<DirectoryEntryData>> rootDse = await _directoryReader.SearchAsync(
            new DirectorySearchQuery(
                domainName,
                string.Empty,
                "(objectClass=*)",
                ["defaultNamingContext"],
                DirectorySearchScope.Base,
                _options.PageSize,
                _options.SearchTimeout,
                connection.Server,
                connection.Credentials),
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
                "The RootDSE did not expose a default naming context.")
            {
                Details = "The server answered LDAP but is not a domain controller for this domain "
                    + "(or an LDS/ADAM instance was addressed).",
            });
    }
}
