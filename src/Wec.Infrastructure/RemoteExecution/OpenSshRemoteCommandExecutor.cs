using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Wec.Core.RemoteExecution;
using Wec.Core.Results;

namespace Wec.Infrastructure.RemoteExecution;

/// <summary>
/// Runs the Windows OpenSSH client without a shell. Batch mode prevents hidden
/// password prompts and strict host-key checking protects the opsi server from
/// impersonation. Administrators must establish trust in known_hosts first.
/// </summary>
public sealed partial class OpenSshRemoteCommandExecutor : IRemoteCommandExecutor
{
    private const int MaximumCapturedCharacters = 32_768;
    private readonly ILogger<OpenSshRemoteCommandExecutor> _logger;

    public OpenSshRemoteCommandExecutor(ILogger<OpenSshRemoteCommandExecutor> logger)
    {
        _logger = logger;
    }

    public async Task<Result<RemoteCommandResult>> ExecuteAsync(
        RemoteCommandRequest request,
        CancellationToken cancellationToken)
    {
        Result<ProcessStartInfo> startInfo = BuildStartInfo(request);
        if (startInfo.IsFailure)
        {
            return Result.Failure<RemoteCommandResult>(startInfo.Error!);
        }

        using var process = new Process { StartInfo = startInfo.Value };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.CommandTimeout);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            process.Start();
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            string output = Truncate(await outputTask);
            string error = Truncate(await errorTask);
            stopwatch.Stop();

            if (process.ExitCode != 0)
            {
                _logger.LogWarning(
                    "SSH command on {Host} exited with code {ExitCode}: {Error}",
                    request.Host,
                    process.ExitCode,
                    error);
                return Result.Failure<RemoteCommandResult>(new Error(
                    ClassifyExitError(error),
                    $"The SSH package operation on '{request.Host}' failed with exit code {process.ExitCode}.")
                {
                    Details = string.IsNullOrWhiteSpace(error) ? output : error,
                });
            }

            return Result.Success(new RemoteCommandResult(
                process.ExitCode,
                output,
                error,
                stopwatch.Elapsed));
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return Result.Failure<RemoteCommandResult>(new Error(
                ErrorCode.ConnectionTimeout,
                $"The SSH package operation on '{request.Host}' timed out or was cancelled."));
        }
        catch (Win32Exception exception)
        {
            return Result.Failure<RemoteCommandResult>(new Error(
                ErrorCode.ServiceUnavailable,
                "Windows OpenSSH (ssh.exe) could not be started. Install the OpenSSH Client optional feature.")
            {
                Details = exception.Message,
            });
        }
        catch (IOException exception)
        {
            TryKill(process);
            return Result.Failure<RemoteCommandResult>(new Error(
                ErrorCode.RemoteCommandFailed,
                $"The SSH process for '{request.Host}' could not be driven.")
            {
                Details = exception.Message,
            });
        }
    }

    internal static Result<ProcessStartInfo> BuildStartInfo(RemoteCommandRequest request)
    {
        if (!IsValidHost(request.Host))
        {
            return Result.Failure<ProcessStartInfo>(new Error(
                ErrorCode.InvalidRequest, "The SSH host is not a valid DNS name or IP address."));
        }

        if (!SshUserPattern().IsMatch(request.UserName))
        {
            return Result.Failure<ProcessStartInfo>(new Error(
                ErrorCode.InvalidRequest, "The configured SSH user name contains unsupported characters."));
        }

        if (string.IsNullOrWhiteSpace(request.Command) || request.Command.Contains('\r') || request.Command.Contains('\n'))
        {
            return Result.Failure<ProcessStartInfo>(new Error(
                ErrorCode.InvalidRequest, "The remote command is empty or contains a line break."));
        }

        if (request.ConnectTimeout <= TimeSpan.Zero || request.CommandTimeout <= TimeSpan.Zero)
        {
            return Result.Failure<ProcessStartInfo>(new Error(
                ErrorCode.InvalidRequest, "SSH timeouts must be greater than zero."));
        }

        string? identityFile = string.IsNullOrWhiteSpace(request.IdentityFile)
            ? null
            : Environment.ExpandEnvironmentVariables(request.IdentityFile);
        if (identityFile is not null && !File.Exists(identityFile))
        {
            return Result.Failure<ProcessStartInfo>(new Error(
                ErrorCode.NotFound, $"The configured SSH identity file was not found: '{identityFile}'."));
        }

        var startInfo = new ProcessStartInfo("ssh.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        AddOption(startInfo, "BatchMode=yes");
        AddOption(startInfo, "StrictHostKeyChecking=yes");
        AddOption(startInfo, "LogLevel=ERROR");
        AddOption(startInfo, $"ConnectTimeout={Math.Max(1, (int)Math.Ceiling(request.ConnectTimeout.TotalSeconds))}");
        if (identityFile is not null)
        {
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(identityFile);
            AddOption(startInfo, "IdentitiesOnly=yes");
        }

        startInfo.ArgumentList.Add($"{request.UserName}@{request.Host}");
        startInfo.ArgumentList.Add(request.Command);
        return Result.Success(startInfo);
    }

    private static void AddOption(ProcessStartInfo startInfo, string value)
    {
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(value);
    }

    private static bool IsValidHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)
            || host.Length > 253
            || host[0] == '-'
            || host.Contains('@')
            || host.Any(char.IsWhiteSpace))
        {
            return false;
        }

        return IPAddress.TryParse(host, out _)
            || Uri.CheckHostName(host) is UriHostNameType.Dns;
    }

    private static ErrorCode ClassifyExitError(string standardError)
    {
        if (standardError.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
        {
            return ErrorCode.AuthenticationFailed;
        }

        return standardError.Contains("Host key verification failed", StringComparison.OrdinalIgnoreCase)
            ? ErrorCode.AuthenticationFailed
            : ErrorCode.RemoteCommandFailed;
    }

    private static string Truncate(string value) =>
        value.Length <= MaximumCapturedCharacters ? value : value[..MaximumCapturedCharacters];

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process never started or already exited.
        }
        catch (Win32Exception)
        {
            // Best effort only; the timeout error is still returned to the caller.
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SshUserPattern();
}
