using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Wec.Core.Messaging;
using Wec.Core.Privileges;
using Wec.Core.Results;

namespace Wec.Host.Bridge;

public sealed record RestartElevatedRequest;

/// <summary>Cancelled = the user dismissed the UAC prompt — a valid outcome, not an error.</summary>
public sealed record RestartElevatedResult(bool Cancelled);

/// <summary>Starts an elevated copy of THIS executable via the UAC prompt.</summary>
internal interface IElevatedProcessLauncher
{
    Result<bool> TryLaunchElevatedCopy();
}

/// <summary>Asks the UI thread to close the main window (ends the app).</summary>
internal interface IAppShutdown
{
    void RequestShutdown();
}

internal sealed partial class ShellElevatedProcessLauncher : IElevatedProcessLauncher
{
    private const int ErrorCancelled = 1223;

    private readonly ILogger<ShellElevatedProcessLauncher> _logger;

    public ShellElevatedProcessLauncher(ILogger<ShellElevatedProcessLauncher> logger)
    {
        _logger = logger;
    }

    public Result<bool> TryLaunchElevatedCopy()
    {
        // Only ever relaunches this very executable — no path crosses the bridge
        string? executablePath = Environment.ProcessPath;
        if (executablePath is null)
        {
            return Result.Failure<bool>(new Error(
                ErrorCode.InternalError, "The path of the running executable could not be determined."));
        }

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true,
                Verb = "runas",
            });
            LogElevatedCopyStarted(executablePath);
            return Result.Success(true);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            LogUacCancelled();
            return Result.Success(false);
        }
        catch (Win32Exception exception)
        {
            _logger.LogWarning(exception, "Starting the elevated copy failed");
            return Result.Failure<bool>(new Error(
                ErrorCode.InternalError, "The elevated restart could not be started.")
            {
                Details = exception.Message,
            });
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Elevated copy started: {ExecutablePath}")]
    private partial void LogElevatedCopyStarted(string executablePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Elevated restart cancelled at the UAC prompt")]
    private partial void LogUacCancelled();
}

internal sealed class MainWindowShutdown : IAppShutdown
{
    private readonly MainWindow _mainWindow;

    public MainWindowShutdown(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public void RequestShutdown() =>
        // Bridge handlers run off the UI thread; Close must be marshaled
        _mainWindow.BeginInvoke(_mainWindow.Close);
}

/// <summary>
/// ADR 0002: the app never elevates itself. This action only automates the
/// documented manual path — the user explicitly clicks, then explicitly
/// confirms the UAC prompt; the unelevated instance closes afterwards.
/// </summary>
internal sealed class RestartElevatedHandler : IActionHandler<RestartElevatedRequest, RestartElevatedResult>
{
    private readonly IPrivilegeContext _privilegeContext;
    private readonly IElevatedProcessLauncher _elevatedProcessLauncher;
    private readonly IAppShutdown _appShutdown;

    public RestartElevatedHandler(
        IPrivilegeContext privilegeContext,
        IElevatedProcessLauncher elevatedProcessLauncher,
        IAppShutdown appShutdown)
    {
        _privilegeContext = privilegeContext;
        _elevatedProcessLauncher = elevatedProcessLauncher;
        _appShutdown = appShutdown;
    }

    public string Module => "system";

    public string Action => "restartElevated";

    public Task<Result<RestartElevatedResult>> HandleAsync(
        RestartElevatedRequest payload,
        CancellationToken cancellationToken)
    {
        if (_privilegeContext.IsElevated)
        {
            return Task.FromResult(Result.Failure<RestartElevatedResult>(new Error(
                ErrorCode.InvalidRequest, "The app is already running as administrator.")));
        }

        Result<bool> launched = _elevatedProcessLauncher.TryLaunchElevatedCopy();
        if (launched.IsFailure)
        {
            return Task.FromResult(Result.Failure<RestartElevatedResult>(launched.Error!));
        }

        if (!launched.Value)
        {
            return Task.FromResult(Result.Success(new RestartElevatedResult(Cancelled: true)));
        }

        _appShutdown.RequestShutdown();
        return Task.FromResult(Result.Success(new RestartElevatedResult(Cancelled: false)));
    }
}
