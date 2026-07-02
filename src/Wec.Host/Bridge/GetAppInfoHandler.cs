using System.IO;
using System.Reflection;
using Microsoft.Extensions.Options;
using Wec.Core.Messaging;
using Wec.Core.Privileges;
using Wec.Core.Results;
using Wec.Infrastructure.Logging;
using Wec.Infrastructure.Persistence;

namespace Wec.Host.Bridge;

public sealed record GetAppInfoRequest;

public sealed record AppInfoResponse(
    string Version,
    string DatabasePath,
    string LogDirectory,
    bool IsElevated);

internal sealed class GetAppInfoHandler : IActionHandler<GetAppInfoRequest, AppInfoResponse>
{
    private readonly DatabaseOptions _databaseOptions;
    private readonly LoggingOptions _loggingOptions;
    private readonly IPrivilegeContext _privilegeContext;

    public GetAppInfoHandler(
        IOptions<DatabaseOptions> databaseOptions,
        IOptions<LoggingOptions> loggingOptions,
        IPrivilegeContext privilegeContext)
    {
        _databaseOptions = databaseOptions.Value;
        _loggingOptions = loggingOptions.Value;
        _privilegeContext = privilegeContext;
    }

    public string Module => "system";

    public string Action => "getAppInfo";

    public Task<Result<AppInfoResponse>> HandleAsync(
        GetAppInfoRequest payload,
        CancellationToken cancellationToken)
    {
        string version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";

        return Task.FromResult(Result.Success(new AppInfoResponse(
            version,
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(_databaseOptions.DatabasePath)),
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(_loggingOptions.LogDirectory)),
            _privilegeContext.IsElevated)));
    }
}
