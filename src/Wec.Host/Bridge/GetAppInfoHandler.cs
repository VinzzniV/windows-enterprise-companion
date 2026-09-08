using System.IO;
using System.Reflection;
using Microsoft.Extensions.Options;
using Wec.Core.Messaging;
using Wec.Core.Privileges;
using Wec.Core.Results;
using Wec.Infrastructure.Logging;
using Wec.Infrastructure.Persistence;
using Wec.Host.Runtime;

namespace Wec.Host.Bridge;

public sealed record GetAppInfoRequest;

public sealed record AppInfoResponse(
    string Version,
    string DatabasePath,
    string LogDirectory,
    bool IsElevated,
    int MaxParallelScans,
    int MaxBatchHosts,
    string MachineName,
    string? MachineFqdn,
    string RuntimeProfile);

internal sealed class GetAppInfoHandler : IActionHandler<GetAppInfoRequest, AppInfoResponse>
{
    private readonly DatabaseOptions _databaseOptions;
    private readonly LoggingOptions _loggingOptions;
    private readonly IPrivilegeContext _privilegeContext;
    private readonly Wec.Core.Targets.RemoteScanOptions _remoteScanOptions;
    private readonly RuntimeInstanceProfile _runtimeProfile;

    public GetAppInfoHandler(
        IOptions<DatabaseOptions> databaseOptions,
        IOptions<LoggingOptions> loggingOptions,
        IPrivilegeContext privilegeContext,
        IOptions<Wec.Core.Targets.RemoteScanOptions> remoteScanOptions,
        RuntimeInstanceProfile runtimeProfile)
    {
        _databaseOptions = databaseOptions.Value;
        _loggingOptions = loggingOptions.Value;
        _privilegeContext = privilegeContext;
        _remoteScanOptions = remoteScanOptions.Value;
        _runtimeProfile = runtimeProfile;
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

        IReadOnlyList<string> localAliases = Wec.Core.Targets.DeviceIdentity.LocalHostAliases();
        string? machineFqdn = localAliases.FirstOrDefault(alias => alias.Contains('.', StringComparison.Ordinal));
        return Task.FromResult(Result.Success(new AppInfoResponse(
            version,
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(_databaseOptions.DatabasePath)),
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(_loggingOptions.LogDirectory)),
            _privilegeContext.IsElevated,
            _remoteScanOptions.MaxParallelScans,
            _remoteScanOptions.MaxBatchHosts,
            Environment.MachineName,
            machineFqdn,
            _runtimeProfile.Name)));
    }
}
