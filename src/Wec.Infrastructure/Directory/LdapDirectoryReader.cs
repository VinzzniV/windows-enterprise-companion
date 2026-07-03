using System.DirectoryServices.Protocols;
using Microsoft.Extensions.Logging;
using Wec.Core.Abstractions;
using Wec.Core.Results;

namespace Wec.Infrastructure.Directory;

/// <summary>
/// LDAP implementation of the read-only directory seam (ADR 0006, revised by
/// ADR 0007). Binds via Negotiate with the current Windows identity or, when
/// the query carries explicit credentials, with those. Pages every search and
/// never chases referrals. Attribute list "1.1" (RFC 4511) requests no
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
        string connectionTarget = query.Server ?? query.DomainDnsName;

        Error? dnsError = ProbeDnsResolution(connectionTarget);
        if (dnsError is not null)
        {
            return Result.Failure<IReadOnlyList<DirectoryEntryData>>(dnsError);
        }

        try
        {
            using var connection = new LdapConnection(new LdapDirectoryIdentifier(connectionTarget));
            connection.AuthType = AuthType.Negotiate;
            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            connection.Timeout = query.TimeLimit;
            if (query.Credentials is { Mode: Wec.Core.Targets.CredentialMode.Explicit } explicitCredentials)
            {
                connection.Credential = new System.Net.NetworkCredential(
                    explicitCredentials.UserName,
                    explicitCredentials.Password,
                    explicitCredentials.Domain);
            }

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

            LogSearchCompleted(entries.Count, query.LdapFilter, connectionTarget);
            return Result.Success<IReadOnlyList<DirectoryEntryData>>(entries);
        }
        catch (LdapException exception)
        {
            Error error = LdapErrorMapper.MapLdapException(exception.ErrorCode, exception.Message, connectionTarget);
            _logger.LogWarning(
                exception, "Directory search failed with {ErrorCode}: {LdapFilter}", error.Code, query.LdapFilter);
            return Result.Failure<IReadOnlyList<DirectoryEntryData>>(error);
        }
        catch (DirectoryOperationException exception)
        {
            Error error = LdapErrorMapper.MapOperationResult(
                exception.Response?.ResultCode, exception.Message, connectionTarget);
            _logger.LogWarning(
                exception, "Directory search failed with {ErrorCode}: {LdapFilter}", error.Code, query.LdapFilter);
            return Result.Failure<IReadOnlyList<DirectoryEntryData>>(error);
        }
    }

    private static Error? ProbeDnsResolution(string target)
    {
        try
        {
            _ = System.Net.Dns.GetHostAddresses(target);
            return null;
        }
        catch (System.Net.Sockets.SocketException exception)
        {
            return new Error(
                ErrorCode.DnsResolutionFailed,
                $"The directory host '{target}' could not be resolved in DNS.")
            {
                Details = "The configured DNS servers do not know this name — on a non-domain "
                    + "network, point DNS at the domain's DNS servers or specify a DC directly. "
                    + exception.Message,
            };
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
            var values = new List<string>();
            foreach (object? value in entry.Attributes[attributeName])
            {
                // S.DS.P sniffs values: UTF-8 decodable ⇒ string, otherwise byte[].
                // Binary values (objectSid, objectGUID) cross the seam as Base64 —
                // DirectoryEntryData.GetBytes is the decoding counterpart.
                values.Add(value switch
                {
                    byte[] binaryValue => Convert.ToBase64String(binaryValue),
                    _ => value?.ToString() ?? string.Empty,
                });
            }

            attributes[attributeName] = values;
        }

        return new DirectoryEntryData(entry.DistinguishedName, attributes);
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Directory search returned {EntryCount} entries for {LdapFilter} against {DomainDnsName}")]
    private partial void LogSearchCompleted(int entryCount, string ldapFilter, string domainDnsName);
}
