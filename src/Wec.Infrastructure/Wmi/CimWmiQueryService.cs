using System.Net.Sockets;
using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Infrastructure.Wmi;

public sealed partial class CimWmiQueryService : IWmiQueryService
{
    private const string QueryDialect = "WQL";

    private readonly ILogger<CimWmiQueryService> _logger;

    public CimWmiQueryService(ILogger<CimWmiQueryService> logger)
    {
        _logger = logger;
    }

    public Task<Result<IReadOnlyList<WmiInstance>>> QueryAsync(
        ScanTarget target,
        ScanCredentials credentials,
        ConnectionOptions connection,
        string wmiNamespace,
        string wqlQuery,
        CancellationToken cancellationToken) =>
        // CIM calls are synchronous; run off the caller thread (UI) instead
        Task.Run(() => ExecuteQuery(target, credentials, connection, wmiNamespace, wqlQuery, cancellationToken), cancellationToken);

    private Result<IReadOnlyList<WmiInstance>> ExecuteQuery(
        ScanTarget target,
        ScanCredentials credentials,
        ConnectionOptions connection,
        string wmiNamespace,
        string wqlQuery,
        CancellationToken cancellationToken)
    {
        if (!target.IsLocal)
        {
            Error? dnsError = ProbeDnsResolution(target.Host!);
            if (dnsError is not null)
            {
                return Result.Failure<IReadOnlyList<WmiInstance>>(dnsError);
            }
        }

        try
        {
            using CimSession session = CreateSession(target, credentials, connection);
            var instances = new List<WmiInstance>();
            foreach (CimInstance cimInstance in session.QueryInstances(wmiNamespace, QueryDialect, wqlQuery))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (cimInstance)
                {
                    instances.Add(ToWmiInstance(cimInstance));
                }
            }

            LogQueryReturned(target.DisplayName, instances.Count, wqlQuery);
            return Result.Success<IReadOnlyList<WmiInstance>>(instances);
        }
        catch (CimException exception)
        {
            Error error = RemoteCimErrorMapper.Map(exception, !target.IsLocal, target.DisplayName);
            _logger.LogWarning(
                exception,
                "CIM query against {Target} failed with {ErrorCode}: {WqlQuery}",
                target.DisplayName,
                error.Code,
                wqlQuery);
            return Result.Failure<IReadOnlyList<WmiInstance>>(error);
        }
    }

    private static CimSession CreateSession(ScanTarget target, ScanCredentials credentials, ConnectionOptions connection)
    {
        if (target.IsLocal)
        {
            return CimSession.Create(null);
        }

        var sessionOptions = new WSManSessionOptions
        {
            Timeout = connection.Timeout,
        };

        if (credentials.Mode == CredentialMode.Explicit)
        {
            using SecureString password = ToSecureString(credentials.Password!);
            sessionOptions.AddDestinationCredentials(new CimCredential(
                PasswordAuthenticationMechanism.Negotiate,
                credentials.Domain,
                credentials.UserName,
                password));
        }

        return CimSession.Create(target.Host, sessionOptions);
    }

    private static Error? ProbeDnsResolution(string host)
    {
        try
        {
            _ = System.Net.Dns.GetHostAddresses(host);
            return null;
        }
        catch (SocketException exception)
        {
            return new Error(
                ErrorCode.DnsResolutionFailed,
                $"The host name '{host}' could not be resolved.")
            {
                Details = exception.Message,
            };
        }
    }

    private static SecureString ToSecureString(string password)
    {
        var secure = new SecureString();
        foreach (char character in password)
        {
            secure.AppendChar(character);
        }

        secure.MakeReadOnly();
        return secure;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "CIM query against {Target} returned {InstanceCount} instances: {WqlQuery}")]
    private partial void LogQueryReturned(string target, int instanceCount, string wqlQuery);

    private static WmiInstance ToWmiInstance(CimInstance cimInstance)
    {
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (CimProperty property in cimInstance.CimInstanceProperties)
        {
            properties[property.Name] = property.Value;
        }

        return new WmiInstance(properties);
    }
}
