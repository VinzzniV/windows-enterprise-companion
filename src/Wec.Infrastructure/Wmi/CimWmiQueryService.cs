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
            // The password must outlive the queries, not just CimSession.Create:
            // WSMan connects and authenticates lazily on the first operation.
            using SecureString? explicitPassword = credentials.Mode == CredentialMode.Explicit
                ? ToSecureString(credentials.Password!)
                : null;
            using CimSession session = CreateSession(target, credentials, connection, explicitPassword);
            using var operationOptions = new CimOperationOptions { Timeout = connection.Timeout };
            var instances = new List<WmiInstance>();
            foreach (CimInstance cimInstance in session.QueryInstances(
                         wmiNamespace,
                         QueryDialect,
                         wqlQuery,
                         operationOptions))
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

    public Task<Result<WmiInstance>> InvokeMethodAsync(
        ScanTarget target,
        ScanCredentials credentials,
        ConnectionOptions connection,
        string wmiNamespace,
        string className,
        string methodName,
        IReadOnlyDictionary<string, object?> inputParameters,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => ExecuteMethod(target, credentials, connection, wmiNamespace, className, methodName, inputParameters, cancellationToken),
            cancellationToken);

    private Result<WmiInstance> ExecuteMethod(
        ScanTarget target,
        ScanCredentials credentials,
        ConnectionOptions connection,
        string wmiNamespace,
        string className,
        string methodName,
        IReadOnlyDictionary<string, object?> inputParameters,
        CancellationToken cancellationToken)
    {
        if (!target.IsLocal)
        {
            Error? dnsError = ProbeDnsResolution(target.Host!);
            if (dnsError is not null)
            {
                return Result.Failure<WmiInstance>(dnsError);
            }
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using SecureString? explicitPassword = credentials.Mode == CredentialMode.Explicit
                ? ToSecureString(credentials.Password!)
                : null;
            using CimSession session = CreateSession(target, credentials, connection, explicitPassword);
            using var operationOptions = new CimOperationOptions { Timeout = connection.Timeout };
            using var parameters = new CimMethodParametersCollection();
            foreach ((string parameterName, object? parameterValue) in inputParameters)
            {
                parameters.Add(CimMethodParameter.Create(parameterName, parameterValue, CimFlags.In));
            }

            using CimMethodResult methodResult = session.InvokeMethod(
                wmiNamespace,
                className,
                methodName,
                parameters,
                operationOptions);
            var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ReturnValue"] = NormalizeValue(methodResult.ReturnValue?.Value),
            };
            foreach (CimMethodParameter outParameter in methodResult.OutParameters)
            {
                properties[outParameter.Name] = NormalizeValue(outParameter.Value);
            }

            return Result.Success(new WmiInstance(properties));
        }
        catch (CimException exception)
        {
            Error error = RemoteCimErrorMapper.Map(exception, !target.IsLocal, target.DisplayName);
            _logger.LogWarning(
                exception,
                "CIM method {ClassName}.{MethodName} against {Target} failed with {ErrorCode}",
                className,
                methodName,
                target.DisplayName,
                error.Code);
            return Result.Failure<WmiInstance>(error);
        }
    }

    private static CimSession CreateSession(
        ScanTarget target,
        ScanCredentials credentials,
        ConnectionOptions connection,
        SecureString? explicitPassword)
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
            sessionOptions.AddDestinationCredentials(new CimCredential(
                PasswordAuthenticationMechanism.Negotiate,
                credentials.Domain,
                credentials.UserName,
                explicitPassword));
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
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            // WMI system-property convention; lets consumers see the concrete
            // class of association endpoints (e.g. Win32_UserAccount vs Win32_Group)
            [WmiInstance.ClassNameProperty] = cimInstance.CimSystemProperties?.ClassName,
        };
        foreach (CimProperty property in cimInstance.CimInstanceProperties)
        {
            properties[property.Name] = NormalizeValue(property.Value);
        }

        return new WmiInstance(properties);
    }

    private static object? NormalizeValue(object? value) => value switch
    {
        // Reference properties (associations like Win32_GroupUser.PartComponent)
        // arrive as nested CimInstances; modules only know the Core WmiInstance type
        CimInstance nestedInstance => ToWmiInstance(nestedInstance),
        CimInstance[] nestedInstances => nestedInstances.Select(ToWmiInstance).ToArray(),
        _ => value,
    };
}
