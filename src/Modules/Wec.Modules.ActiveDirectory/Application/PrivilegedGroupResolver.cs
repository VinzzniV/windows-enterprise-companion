using System.Security.Principal;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;

namespace Wec.Modules.ActiveDirectory.Application;

internal sealed record ResolvedPrivilegedGroup(string GroupName, string DistinguishedName);

internal sealed class PrivilegedGroupResolver
{
    private const string BuiltinAdministratorsSid = "S-1-5-32-544";
    private const int DomainAdminsRid = 512;
    private const int SchemaAdminsRid = 518;
    private const int EnterpriseAdminsRid = 519;

    private readonly IDirectoryReader _directoryReader;
    private readonly ActiveDirectoryOptions _options;

    public PrivilegedGroupResolver(
        IDirectoryReader directoryReader,
        IOptions<ActiveDirectoryOptions> options)
    {
        _directoryReader = directoryReader;
        _options = options.Value;
    }

    public async Task<Result<IReadOnlyList<ResolvedPrivilegedGroup>>> ResolveAsync(
        string domainName,
        string namingContext,
        DirectoryConnection connection,
        CancellationToken cancellationToken)
    {
        Result<string> domainSid = await ReadDomainSidAsync(
            domainName, namingContext, connection, cancellationToken);
        if (domainSid.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ResolvedPrivilegedGroup>>(domainSid.Error!);
        }

        string[] privilegedSids =
        [
            $"{domainSid.Value}-{DomainAdminsRid}",
            $"{domainSid.Value}-{EnterpriseAdminsRid}",
            $"{domainSid.Value}-{SchemaAdminsRid}",
            BuiltinAdministratorsSid,
        ];

        var groups = new List<ResolvedPrivilegedGroup>();
        foreach (string sid in privilegedSids)
        {
            Result<IReadOnlyList<DirectoryEntryData>> entries = await _directoryReader.SearchAsync(
                new DirectorySearchQuery(
                    domainName,
                    namingContext,
                    AdFilters.GroupBySid(sid),
                    ["sAMAccountName"],
                    DirectorySearchScope.Subtree,
                    _options.PageSize,
                    _options.SearchTimeout,
                    connection.Server,
                    connection.Credentials),
                cancellationToken);
            if (entries.IsFailure)
            {
                return Result.Failure<IReadOnlyList<ResolvedPrivilegedGroup>>(entries.Error!);
            }

            // Enterprise/Schema Admins only exist on the forest root. An absent
            // group is valid for child domains and therefore not an error.
            if (entries.Value.Count == 0)
            {
                continue;
            }

            DirectoryEntryData group = entries.Value[0];
            groups.Add(new ResolvedPrivilegedGroup(
                group.GetFirstValue("sAMAccountName") ?? group.DistinguishedName,
                group.DistinguishedName));
        }

        return Result.Success<IReadOnlyList<ResolvedPrivilegedGroup>>(groups);
    }

    private async Task<Result<string>> ReadDomainSidAsync(
        string domainName,
        string namingContext,
        DirectoryConnection connection,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<DirectoryEntryData>> domainHead = await _directoryReader.SearchAsync(
            new DirectorySearchQuery(
                domainName,
                namingContext,
                "(objectClass=*)",
                ["objectSid"],
                DirectorySearchScope.Base,
                _options.PageSize,
                _options.SearchTimeout,
                connection.Server,
                connection.Credentials),
            cancellationToken);
        if (domainHead.IsFailure)
        {
            return Result.Failure<string>(domainHead.Error!);
        }

        byte[]? sidBytes = domainHead.Value.Count > 0 ? domainHead.Value[0].GetBytes("objectSid") : null;
        if (sidBytes is null)
        {
            return Result.Failure<string>(new Error(
                ErrorCode.DirectoryUnavailable,
                "The domain head did not expose a readable objectSid."));
        }

        try
        {
            return Result.Success(new SecurityIdentifier(sidBytes, 0).Value);
        }
        catch (ArgumentException)
        {
            return Result.Failure<string>(new Error(
                ErrorCode.DirectoryUnavailable,
                "The domain head exposed an invalid objectSid."));
        }
    }
}
