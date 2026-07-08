using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Wec.Core.Messaging;
using Wec.Core.Results;

namespace Wec.Host.Bridge;

/// <param name="Host">The client to open an interactive remote session against.</param>
/// <param name="UserName">Admin account; null/empty = the current user.</param>
public sealed record OpenPsSessionRequest(string Host, string? UserName, string? Domain, string? Password);

public sealed record OpenPsSessionResponse(bool Launched);

public sealed record PowerShellSessionSpec(string Host, string? UserName, string? Domain, string? Password);

/// <summary>Opens an interactive PowerShell remoting session in a new console window.</summary>
internal interface IPowerShellSessionLauncher
{
    Result<bool> Launch(PowerShellSessionSpec spec);
}

/// <summary>
/// ADR 0011: a human-driven, per-click PowerShell remoting session — the one
/// deliberate execution path in the otherwise read-only app. The admin
/// credentials come from the session sign-in and are handed to the child
/// process via an environment variable (never the command line, never disk,
/// never the log), which the bootstrap clears immediately after building the
/// PSCredential.
/// </summary>
internal sealed partial class ShellPowerShellSessionLauncher : IPowerShellSessionLauncher
{
    // Read once by the bootstrap, then removed from the child's environment.
    private const string PasswordEnvVar = "WEC_PSS_PW";

    private readonly ILogger<ShellPowerShellSessionLauncher> _logger;

    public ShellPowerShellSessionLauncher(ILogger<ShellPowerShellSessionLauncher> logger)
    {
        _logger = logger;
    }

    public Result<bool> Launch(PowerShellSessionSpec spec)
    {
        string host = spec.Host.Trim();
        bool withCredentials = !string.IsNullOrWhiteSpace(spec.UserName);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false, // required to set Environment; also gives the child its own console
            CreateNoWindow = false,
        };
        startInfo.ArgumentList.Add("-NoExit");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(BuildBootstrap(host, spec, withCredentials));

        if (withCredentials)
        {
            // Per-process env var — invisible to other processes' command-line inspection.
            startInfo.Environment[PasswordEnvVar] = spec.Password ?? string.Empty;
        }

        try
        {
            using Process? process = Process.Start(startInfo);
            LogSessionStarted(host, withCredentials);
            return Result.Success(true);
        }
        catch (Win32Exception exception)
        {
            _logger.LogWarning(exception, "Opening a PowerShell session to {Host} failed", host);
            return Result.Failure<bool>(new Error(
                ErrorCode.InternalError, "The PowerShell session could not be started.")
            {
                Details = exception.Message,
            });
        }
    }

    private static string BuildBootstrap(string host, PowerShellSessionSpec spec, bool withCredentials)
    {
        string safeHost = SingleQuote(host);
        if (!withCredentials)
        {
            return
                $"$ErrorActionPreference='Stop'; try {{ Enter-PSSession -ComputerName {safeHost} }} " +
                "catch { Write-Host ('Connection failed: ' + $_.Exception.Message) -ForegroundColor Red }";
        }

        string account = string.IsNullOrWhiteSpace(spec.Domain)
            ? spec.UserName!.Trim()
            : $"{spec.Domain!.Trim()}\\{spec.UserName!.Trim()}";

        return
            $"$ErrorActionPreference='Stop'; try {{ " +
            $"$u={SingleQuote(account)}; " +
            $"$p=ConvertTo-SecureString $env:{PasswordEnvVar} -AsPlainText -Force; " +
            $"Remove-Item Env:{PasswordEnvVar} -ErrorAction SilentlyContinue; " +
            "$c=New-Object System.Management.Automation.PSCredential($u,$p); " +
            $"Enter-PSSession -ComputerName {safeHost} -Credential $c }} " +
            "catch { Write-Host ('Connection failed: ' + $_.Exception.Message) -ForegroundColor Red }";
    }

    // PowerShell single-quoted literal — the only escape needed is doubling single quotes.
    private static string SingleQuote(string value) => $"'{value.Replace("'", "''")}'";

    [LoggerMessage(Level = LogLevel.Information, Message = "PowerShell session opened to {Host} (explicit credentials: {WithCredentials})")]
    private partial void LogSessionStarted(string host, bool withCredentials);
}

internal sealed class OpenPsSessionHandler : IActionHandler<OpenPsSessionRequest, OpenPsSessionResponse>
{
    private readonly IPowerShellSessionLauncher _launcher;

    public OpenPsSessionHandler(IPowerShellSessionLauncher launcher)
    {
        _launcher = launcher;
    }

    public string Module => "system";

    public string Action => "openPsSession";

    public Task<Result<OpenPsSessionResponse>> HandleAsync(
        OpenPsSessionRequest payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload.Host))
        {
            return Task.FromResult(Result.Failure<OpenPsSessionResponse>(new Error(
                ErrorCode.InvalidRequest, "A host is required to open a PowerShell session.")));
        }

        Result<bool> launched = _launcher.Launch(new PowerShellSessionSpec(
            payload.Host, payload.UserName, payload.Domain, payload.Password));

        return Task.FromResult(launched.IsSuccess
            ? Result.Success(new OpenPsSessionResponse(launched.Value))
            : Result.Failure<OpenPsSessionResponse>(launched.Error!));
    }
}
