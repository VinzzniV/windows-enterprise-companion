using System.DirectoryServices.Protocols;
using Microsoft.Extensions.Logging;
using Wec.Core.Abstractions;
using Wec.Core.Results;

namespace Wec.Infrastructure.Directory;

/// <summary>
/// LDAP implementation of the read-only directory seam (ADR 0006).
/// Binds with the current Windows identity (Negotiate), pages every search
/// and never chases referrals. Attribute list "1.1" (RFC 4511) requests no
/// attributes — used when the caller only counts entries.
/// </summary>
public sealed partial class LdapDirectoryReader : IDirectoryReader
{
    private const string NoAttributesMarker = "1.1";

    private readonly ILogger<LdapDirectoryReader> _logger;

    public LdapDirectoryReader(ILogger<LdapDirectoryReader> logger)
    {
        _logger = logger;
    }

    public Task<Result<IReadOnlyList<DirectoryEntryData>>> SearchAsync(
        DirectorySearchQuery query,
        CancellationToken cancellationToken) =>
        // S.DS.Protocols is synchronous; run off the caller thread (UI)
        Task.Run(() => ExecuteSearch(query), cancellationToken);

    private Result<IReadOnlyList<DirectoryEntryData>> ExecuteSearch(DirectorySearchQuery query)
    {
        try
        {
            using var connection = new LdapConnection(new LdapDirectoryIdentifier(query.DomainDnsName));
            connection.AuthType = AuthType.Negotiate;
            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            connection.Timeout = query.TimeLimit;
            connection.Bind();

            string[] attributes = query.Attributes.Count > 0
                ? [.. query.Attributes]
                : [NoAttributesMarker];
            var request = new SearchRequest(
                query.BaseDistinguishedName,
                query.LdapFilter,
                MapScope(query.Scope),
                attributes);
            var pageControl = new PageResultRequestControl(query.PageSize);
            request.Controls.Add(pageControl);

            var entries = new List<DirectoryEntryData>();
            while (true)
            {
                var response = (SearchResponse)connection.SendRequest(request, query.TimeLimit);
                foreach (SearchResultEntry entry in response.Entries)
                {
                    entries.Add(ToEntryData(entry));
                }

                PageResultResponseControl? pageResponse = response.Controls
                    .OfType<PageResultResponseControl>()
                    .FirstOrDefault();
                if (pageResponse is null || pageResponse.Cookie.Length == 0)
                {
                    break;
                }

                pageControl.Cookie = pageResponse.Cookie;
            }

            LogSearchCompleted(entries.Count, query.LdapFilter, query.DomainDnsName);
            return Result.Success<IReadOnlyList<DirectoryEntryData>>(entries);
        }
        catch (DirectoryOperationException exception)
            when (exception.Response?.ResultCode == ResultCode.InsufficientAccessRights)
        {
            _logger.LogWarning(exception, "Directory search denied: {LdapFilter}", query.LdapFilter);
            return Result.Failure<IReadOnlyList<DirectoryEntryData>>(new Error(
                ErrorCode.AccessDenied,
                "The directory refused the read with the current credentials.")
            {
                Details = exception.Message,
            });
        }
        catch (Exception exception) when (exception is LdapException or DirectoryOperationException)
        {
            _logger.LogWarning(exception, "Directory search failed: {LdapFilter}", query.LdapFilter);
            return Result.Failure<IReadOnlyList<DirectoryEntryData>>(new Error(
                ErrorCode.DirectoryUnavailable,
                $"The directory for '{query.DomainDnsName}' could not be queried.")
            {
                Details = exception.Message,
            });
        }
    }

    private static SearchScope MapScope(DirectorySearchScope scope) => scope switch
    {
        DirectorySearchScope.Base => SearchScope.Base,
        DirectorySearchScope.OneLevel => SearchScope.OneLevel,
        _ => SearchScope.Subtree,
    };

    private static DirectoryEntryData ToEntryData(SearchResultEntry entry)
    {
        var attributes = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string attributeName in entry.Attributes.AttributeNames)
        {
            attributes[attributeName] =
                [.. entry.Attributes[attributeName].GetValues(typeof(string)).Cast<string>()];
        }

        return new DirectoryEntryData(entry.DistinguishedName, attributes);
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Directory search returned {EntryCount} entries for {LdapFilter} against {DomainDnsName}")]
    private partial void LogSearchCompleted(int entryCount, string ldapFilter, string domainDnsName);
}
