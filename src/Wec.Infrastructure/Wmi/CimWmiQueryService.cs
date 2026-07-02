using Microsoft.Extensions.Logging;
using Microsoft.Management.Infrastructure;
using Wec.Core.Abstractions;
using Wec.Core.Privileges;
using Wec.Core.Results;

namespace Wec.Infrastructure.Wmi;

public sealed class CimWmiQueryService : IWmiQueryService
{
    private const string QueryDialect = "WQL";

    private readonly ILogger<CimWmiQueryService> _logger;

    public CimWmiQueryService(ILogger<CimWmiQueryService> logger)
    {
        _logger = logger;
    }

    public Task<Result<IReadOnlyList<WmiInstance>>> QueryAsync(
        string wmiNamespace,
        string wqlQuery,
        CancellationToken cancellationToken) =>
        // CIM calls are synchronous; run off the caller thread (UI) instead
        Task.Run(() => ExecuteQuery(wmiNamespace, wqlQuery), cancellationToken);

    private Result<IReadOnlyList<WmiInstance>> ExecuteQuery(string wmiNamespace, string wqlQuery)
    {
        try
        {
            using CimSession session = CimSession.Create(null);
            var instances = new List<WmiInstance>();
            foreach (CimInstance cimInstance in session.QueryInstances(wmiNamespace, QueryDialect, wqlQuery))
            {
                using (cimInstance)
                {
                    instances.Add(ToWmiInstance(cimInstance));
                }
            }

            _logger.LogDebug("WMI query returned {InstanceCount} instances: {WqlQuery}", instances.Count, wqlQuery);
            return Result.Success<IReadOnlyList<WmiInstance>>(instances);
        }
        catch (CimException exception) when (exception.NativeErrorCode == NativeErrorCode.AccessDenied)
        {
            _logger.LogWarning(exception, "WMI query access denied: {WqlQuery}", wqlQuery);
            return Result.Failure<IReadOnlyList<WmiInstance>>(Error.AccessDenied(
                $"Access to WMI namespace '{wmiNamespace}' was denied.",
                PrivilegeLevel.Administrator));
        }
        catch (CimException exception)
        {
            _logger.LogError(exception, "WMI query failed: {WqlQuery}", wqlQuery);
            return Result.Failure<IReadOnlyList<WmiInstance>>(Error.WmiUnavailable(
                "The WMI query failed.",
                exception.Message));
        }
    }

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
