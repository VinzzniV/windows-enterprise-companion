using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly ILogger<OpenLogsFolderHandler> _logger;

    public OpenLogsFolderHandler(IOptions<LoggingOptions> loggingOptions, ILogger<OpenLogsFolderHandler> logger)
    {
        _loggingOptions = loggingOptions.Value;
        _logger = logger;
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

        using Process? process = Process.Start(new ProcessStartInfo
        {
            FileName = logDirectory,
            UseShellExecute = true,
        });

        _logger.LogInformation("Opened log directory {LogDirectory} in shell", logDirectory);
        return Task.FromResult(Result.Success(new OpenLogsFolderResponse(logDirectory)));
    }
}
