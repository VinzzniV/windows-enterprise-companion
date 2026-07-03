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
    bool IsElevated,
    int MaxParallelScans);

internal sealed class GetAppInfoHandler : IActionHandler<GetAppInfoRequest, AppInfoResponse>
{
    private readonly DatabaseOptions _databaseOptions;
    private readonly LoggingOptions _loggingOptions;
    private readonly IPrivilegeContext _privilegeContext;
    private readonly Wec.Core.Targets.RemoteScanOptions _remoteScanOptions;

    public GetAppInfoHandler(
        IOptions<DatabaseOptions> databaseOptions,
        IOptions<LoggingOptions> loggingOptions,
        IPrivilegeContext privilegeContext,
        IOptions<Wec.Core.Targets.RemoteScanOptions> remoteScanOptions)
    {
        _databaseOptions = databaseOptions.Value;
        _loggingOptions = loggingOptions.Value;
        _privilegeContext = privilegeContext;
        _remoteScanOptions = remoteScanOptions.Value;
    }

    public string Module => "system";

    public string Action => "getAppInfo";

    public Task<Result<AppInfoResponse>> HandleAsync(
        GetAppInfoRequest payload,
        CancellationToken cancellationToken)
    {
        string informationalVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";
        // The SDK appends "+<git commit hash>" build metadata; not display-worthy
        string version = informationalVersion.Split('+')[0];

        return Task.FromResult(Result.Success(new AppInfoResponse(
            version,
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(_databaseOptions.DatabasePath)),
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(_loggingOptions.LogDirectory)),
            _privilegeContext.IsElevated,
            _remoteScanOptions.MaxParallelScans)));
    }
}
