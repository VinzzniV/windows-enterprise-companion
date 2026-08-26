using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Wec.Core.RemoteExecution;
using Wec.Core.Results;

namespace Wec.Infrastructure.RemoteExecution;

public sealed partial class OpenSshRemoteFileUploader : IRemoteFileUploader
{
    public async Task<Result<RemoteFileUploadResult>> UploadAsync(
        RemoteFileUploadRequest request,
        CancellationToken cancellationToken)
    {
        Result<bool> validation = Validate(request);
        if (validation.IsFailure)
        {
            return Result.Failure<RemoteFileUploadResult>(validation.Error!);
        }

        FileInfo file = new(request.LocalPath);
        string sha256;
        try
        {
            await using FileStream stream = file.OpenRead();
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            sha256 = Convert.ToHexStringLower(hash);
        }
        catch (IOException exception)
        {
            return Result.Failure<RemoteFileUploadResult>(new Error(
                ErrorCode.ServiceUnavailable,
                "The generated opsi source archive could not be read.") { Details = exception.Message });
        }

        Result<ProcessStartInfo> startInfo = BuildStartInfo(request);
        if (startInfo.IsFailure)
        {
            return Result.Failure<RemoteFileUploadResult>(startInfo.Error!);
        }

        using var process = new Process { StartInfo = startInfo.Value };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.TransferTimeout);
        try
        {
            process.Start();
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            string standardError = await error.ConfigureAwait(false);
            _ = await output.ConfigureAwait(false);
            return process.ExitCode == 0
                ? Result.Success(new RemoteFileUploadResult(request.RemotePath, file.Length, sha256))
                : Result.Failure<RemoteFileUploadResult>(new Error(
                    ErrorCode.RemoteCommandFailed,
                    $"The generated opsi source archive could not be uploaded to '{request.Host}'.")
                {
                    Details = standardError,
                });
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return Result.Failure<RemoteFileUploadResult>(new Error(
                ErrorCode.ConnectionTimeout,
                $"The opsi source upload to '{request.Host}' timed out or was cancelled."));
        }
        catch (Win32Exception exception)
        {
            return Result.Failure<RemoteFileUploadResult>(new Error(
                ErrorCode.ServiceUnavailable,
                "Windows OpenSSH (scp.exe) could not be started.") { Details = exception.Message });
        }
    }

    internal static Result<bool> Validate(RemoteFileUploadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LocalPath)
            || !File.Exists(request.LocalPath)
            || !IsValidHost(request.Host)
            || !SshUserPattern().IsMatch(request.UserName)
            || !RemotePathPattern().IsMatch(request.RemotePath)
            || request.ConnectTimeout <= TimeSpan.Zero
            || request.TransferTimeout <= TimeSpan.Zero)
        {
            return Result.Failure<bool>(new Error(
                ErrorCode.InvalidRequest,
                "The local archive, SSH target or remote temporary path is invalid."));
        }

        return Result.Success(true);
    }

    private static Result<ProcessStartInfo> BuildStartInfo(RemoteFileUploadRequest request)
    {
        var startInfo = new ProcessStartInfo("scp.exe")
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
        if (!string.IsNullOrWhiteSpace(request.IdentityFile))
        {
            string identityFile = Environment.ExpandEnvironmentVariables(request.IdentityFile);
            if (!File.Exists(identityFile))
            {
                return Result.Failure<ProcessStartInfo>(Error.NotFound(
                    $"The configured SSH identity file was not found: '{identityFile}'."));
            }

            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(identityFile);
            AddOption(startInfo, "IdentitiesOnly=yes");
        }

        startInfo.ArgumentList.Add(request.LocalPath);
        startInfo.ArgumentList.Add($"{request.UserName}@{request.Host}:{request.RemotePath}");
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

        return IPAddress.TryParse(host, out _) || Uri.CheckHostName(host) is UriHostNameType.Dns;
    }

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
        }
        catch (Win32Exception)
        {
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SshUserPattern();

    [GeneratedRegex("^/tmp/wec-winget-[a-f0-9]{32}\\.tar\\.gz$", RegexOptions.CultureInvariant)]
    private static partial Regex RemotePathPattern();
}
