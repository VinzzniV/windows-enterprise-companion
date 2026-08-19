using System.IO;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Infrastructure.Logging;

namespace Wec.Host.Bridge;

public sealed record OpenLogsFolderRequest;

public sealed record OpenLogsFolderResponse(string LogDirectory);

/// <summary>
/// Opens the log directory in Explorer. The path comes exclusively from the
/// validated logging options — never from the request payload — so the bridge
/// cannot be used to open arbitrary paths.
/// </summary>
internal sealed class OpenLogsFolderHandler : IActionHandler<OpenLogsFolderRequest, OpenLogsFolderResponse>
{
    private readonly LoggingOptions _loggingOptions;
    private readonly IShellLauncher _shellLauncher;

    public OpenLogsFolderHandler(IOptions<LoggingOptions> loggingOptions, IShellLauncher shellLauncher)
    {
        _loggingOptions = loggingOptions.Value;
        _shellLauncher = shellLauncher;
    }

    public string Module => "system";

    public string Action => "openLogsFolder";

    public Task<Result<OpenLogsFolderResponse>> HandleAsync(
        OpenLogsFolderRequest payload,
        CancellationToken cancellationToken)
    {
        string logDirectory = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(_loggingOptions.LogDirectory));

        if (!Directory.Exists(logDirectory))
        {
            return Task.FromResult(Result.Failure<OpenLogsFolderResponse>(
                Error.NotFound($"Log directory does not exist: {logDirectory}")));
        }

        if (!_shellLauncher.TryOpenPath(logDirectory))
        {
            return Task.FromResult(Result.Failure<OpenLogsFolderResponse>(new Error(
                ErrorCode.InternalError,
                "The log directory could not be opened. Review the application log for details.")));
        }

        return Task.FromResult(Result.Success(new OpenLogsFolderResponse(logDirectory)));
    }
}
