using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Wec.Core.RemoteExecution;
using Wec.Core.Results;

namespace Wec.Infrastructure.RemoteExecution;

public sealed partial class HttpOpenSshArtifactStager : IRemoteArtifactStager, IDisposable
{
    private const int MaximumReleaseResponseBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpOpenSshArtifactStager> _logger;

    public HttpOpenSshArtifactStager(ILogger<HttpOpenSshArtifactStager> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WindowsEnterpriseCompanion/1.0");
    }

    public async Task<Result<RemoteArtifactStageResult>> StageAsync(
        RemoteArtifactStageRequest request,
        CancellationToken cancellationToken)
    {
        Result<bool> validation = Validate(request);
        if (validation.IsFailure)
        {
            return Result.Failure<RemoteArtifactStageResult>(validation.Error!);
        }

        string temporaryPath = Path.Combine(Path.GetTempPath(), $"wec-{Guid.NewGuid():N}.artifact");
        try
        {
            Result<Uri> artifactUrl = await ResolveArtifactUrlAsync(request, cancellationToken);
            if (artifactUrl.IsFailure)
            {
                return Result.Failure<RemoteArtifactStageResult>(artifactUrl.Error!);
            }

            Result<(long Length, string Sha256)> download = await DownloadAsync(
                artifactUrl.Value,
                temporaryPath,
                request.MaximumArtifactBytes,
                request.TransferTimeout,
                cancellationToken);
            if (download.IsFailure)
            {
                return Result.Failure<RemoteArtifactStageResult>(download.Error!);
            }

            Result<bool> upload = await UploadAsync(temporaryPath, request, cancellationToken);
            return upload.IsFailure
                ? Result.Failure<RemoteArtifactStageResult>(upload.Error!)
                : Result.Success(new RemoteArtifactStageResult(
                    artifactUrl.Value,
                    request.RemotePath,
                    download.Value.Length,
                    download.Value.Sha256));
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException exception)
            {
                _logger.LogWarning(exception, "Could not remove temporary package artifact {Path}", temporaryPath);
            }
            catch (UnauthorizedAccessException exception)
            {
                _logger.LogWarning(exception, "Could not remove temporary package artifact {Path}", temporaryPath);
            }
        }
    }

    public async Task<Result<RemoteArtifactTransferResult>> TransferAsync(
        RemoteArtifactTransferRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsValidHost(request.SourceHost)
            || !IsValidHost(request.TargetHost)
            || !SshUserPattern().IsMatch(request.UserName)
            || !ApprovedPackagePathPattern().IsMatch(request.RemotePath)
            || request.ConnectTimeout <= TimeSpan.Zero
            || request.TransferTimeout <= TimeSpan.Zero)
        {
            return Result.Failure<RemoteArtifactTransferResult>(new Error(
                ErrorCode.InvalidRequest,
                "The approved package transfer target is invalid."));
        }

        Result<ProcessStartInfo> startInfo = TryBuildScpStartInfo(
            request.UserName,
            request.ConnectTimeout,
            request.IdentityFile);
        if (startInfo.IsFailure)
        {
            return Result.Failure<RemoteArtifactTransferResult>(startInfo.Error!);
        }

        startInfo.Value.ArgumentList.Add("-3");
        startInfo.Value.ArgumentList.Add($"{request.UserName}@{request.SourceHost}:{request.RemotePath}");
        startInfo.Value.ArgumentList.Add($"{request.UserName}@{request.TargetHost}:{request.RemotePath}");
        Result<bool> transfer = await RunScpAsync(
            startInfo.Value,
            request.TargetHost,
            request.TransferTimeout,
            cancellationToken).ConfigureAwait(false);
        return transfer.IsFailure
            ? Result.Failure<RemoteArtifactTransferResult>(transfer.Error!)
            : Result.Success(new RemoteArtifactTransferResult(
                request.SourceHost,
                request.TargetHost,
                request.RemotePath));
    }

    internal static Result<bool> Validate(RemoteArtifactStageRequest request)
    {
        if (request.ReleaseUrl.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(request.ArtifactUrlPattern))
        {
            return Result.Failure<bool>(new Error(
                ErrorCode.InvalidRequest,
                "Package automation requires an HTTPS release URL and an artifact pattern."));
        }

        if (request.MaximumArtifactBytes is <= 0 or > 2L * 1024 * 1024 * 1024)
        {
            return Result.Failure<bool>(new Error(
                ErrorCode.InvalidRequest,
                "The package artifact size limit must be between 1 byte and 2 GB."));
        }

        if (!IsValidHost(request.Host)
            || !SshUserPattern().IsMatch(request.UserName)
            || !RemotePathPattern().IsMatch(request.RemotePath)
            || request.ConnectTimeout <= TimeSpan.Zero
            || request.TransferTimeout <= TimeSpan.Zero)
        {
            return Result.Failure<bool>(new Error(
                ErrorCode.InvalidRequest,
                "The SSH target or remote staging path is invalid."));
        }

        return Result.Success(true);
    }

    private async Task<Result<Uri>> ResolveArtifactUrlAsync(
        RemoteArtifactStageRequest request,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.TransferTimeout);
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(
                request.ReleaseUrl,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<Uri>(new Error(
                    ErrorCode.ServiceUnavailable,
                    $"The package release source answered HTTP {(int)response.StatusCode}."));
            }

            if (response.Content.Headers.ContentLength > MaximumReleaseResponseBytes)
            {
                return Result.Failure<Uri>(new Error(
                    ErrorCode.InvalidRequest,
                    "The package release response exceeds the 2 MB safety limit."));
            }

            await using Stream responseStream = await response.Content
                .ReadAsStreamAsync(timeout.Token)
                .ConfigureAwait(false);
            using var contentBuffer = new MemoryStream(MaximumReleaseResponseBytes);
            byte[] chunk = new byte[81920];
            int read;
            while ((read = await responseStream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (contentBuffer.Length + read > MaximumReleaseResponseBytes)
                {
                    return Result.Failure<Uri>(new Error(
                        ErrorCode.InvalidRequest,
                        "The package release response exceeds the 2 MB safety limit."));
                }

                await contentBuffer.WriteAsync(chunk.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
            }

            string content = Encoding.UTF8.GetString(
                contentBuffer.GetBuffer(),
                0,
                checked((int)contentBuffer.Length));
            var expression = new Regex(
                request.ArtifactUrlPattern,
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                RegexTimeout);
            Match match = expression.Match(content);
            return match.Success
                && match.Groups.Count > 1
                && Uri.TryCreate(match.Groups[1].Value, UriKind.Absolute, out Uri? artifactUrl)
                && artifactUrl.Scheme == Uri.UriSchemeHttps
                    ? Result.Success(artifactUrl)
                    : Result.Failure<Uri>(new Error(
                        ErrorCode.NotFound,
                        "The artifact pattern did not resolve exactly one HTTPS download URL."));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<Uri>(new Error(
                ErrorCode.ConnectionTimeout,
                "The package release source timed out."));
        }
        catch (HttpRequestException exception)
        {
            return Result.Failure<Uri>(new Error(
                ErrorCode.ServiceUnavailable,
                "The package release source could not be reached.") { Details = exception.Message });
        }
        catch (IOException exception)
        {
            return Result.Failure<Uri>(new Error(
                ErrorCode.ServiceUnavailable,
                "The package release response could not be read.") { Details = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<Uri>(new Error(
                ErrorCode.InvalidRequest,
                "The package artifact pattern is invalid.") { Details = exception.Message });
        }
        catch (RegexMatchTimeoutException)
        {
            return Result.Failure<Uri>(new Error(
                ErrorCode.InvalidRequest,
                "The package artifact pattern exceeded the safety timeout."));
        }
    }

    private async Task<Result<(long Length, string Sha256)>> DownloadAsync(
        Uri artifactUrl,
        string destination,
        long maximumBytes,
        TimeSpan transferTimeout,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(transferTimeout);
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(
                artifactUrl,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<(long, string)>(new Error(
                    ErrorCode.ServiceUnavailable,
                    $"The package artifact answered HTTP {(int)response.StatusCode}."));
            }

            if (response.Content.Headers.ContentLength > maximumBytes)
            {
                return Result.Failure<(long, string)>(new Error(
                    ErrorCode.InvalidRequest,
                    "The package artifact exceeds the configured size limit."));
            }

            await using Stream source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            await using var destinationStream = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[81920];
            long length = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                length += read;
                if (length > maximumBytes)
                {
                    return Result.Failure<(long, string)>(new Error(
                        ErrorCode.InvalidRequest,
                        "The package artifact exceeds the configured size limit."));
                }

                hash.AppendData(buffer, 0, read);
                await destinationStream.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
            }

            return Result.Success((length, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<(long, string)>(new Error(
                ErrorCode.ConnectionTimeout,
                "The package artifact download timed out."));
        }
        catch (HttpRequestException exception)
        {
            return Result.Failure<(long, string)>(new Error(
                ErrorCode.ServiceUnavailable,
                "The package artifact could not be downloaded.") { Details = exception.Message });
        }
        catch (IOException exception)
        {
            return Result.Failure<(long, string)>(new Error(
                ErrorCode.RemoteCommandFailed,
                "The temporary package artifact could not be written.") { Details = exception.Message });
        }
    }

    private static async Task<Result<bool>> UploadAsync(
        string localPath,
        RemoteArtifactStageRequest request,
        CancellationToken cancellationToken)
    {
        Result<ProcessStartInfo> startInfo = TryBuildScpStartInfo(
            request.UserName,
            request.ConnectTimeout,
            request.IdentityFile);
        if (startInfo.IsFailure)
        {
            return Result.Failure<bool>(startInfo.Error!);
        }

        startInfo.Value.ArgumentList.Add(localPath);
        startInfo.Value.ArgumentList.Add($"{request.UserName}@{request.Host}:{request.RemotePath}");
        return await RunScpAsync(
            startInfo.Value,
            request.Host,
            request.TransferTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    private static Result<ProcessStartInfo> TryBuildScpStartInfo(
        string userName,
        TimeSpan connectTimeout,
        string? identityFile)
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
        AddOption(startInfo, $"ConnectTimeout={Math.Max(1, (int)Math.Ceiling(connectTimeout.TotalSeconds))}");
        if (!string.IsNullOrWhiteSpace(identityFile))
        {
            string expandedIdentityFile = Environment.ExpandEnvironmentVariables(identityFile);
            if (!File.Exists(expandedIdentityFile))
            {
                return Result.Failure<ProcessStartInfo>(Error.NotFound(
                    $"The configured SSH identity file was not found: '{expandedIdentityFile}'."));
            }

            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(expandedIdentityFile);
            AddOption(startInfo, "IdentitiesOnly=yes");
        }

        return Result.Success(startInfo);
    }

    private static async Task<Result<bool>> RunScpAsync(
        ProcessStartInfo startInfo,
        string targetHost,
        TimeSpan transferTimeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(transferTimeout);
        try
        {
            process.Start();
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            string standardError = await error.ConfigureAwait(false);
            _ = await output.ConfigureAwait(false);
            return process.ExitCode == 0
                ? Result.Success(true)
                : Result.Failure<bool>(new Error(
                    ErrorCode.RemoteCommandFailed,
                    $"The package artifact transfer to '{targetHost}' failed with exit code {process.ExitCode}.")
                {
                    Details = standardError,
                });
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return Result.Failure<bool>(new Error(
                ErrorCode.ConnectionTimeout,
                $"The package artifact transfer to '{targetHost}' timed out or was cancelled."));
        }
        catch (Win32Exception exception)
        {
            return Result.Failure<bool>(new Error(
                ErrorCode.ServiceUnavailable,
                "Windows OpenSSH (scp.exe) could not be started.") { Details = exception.Message });
        }
    }

    private static void AddOption(ProcessStartInfo startInfo, string value)
    {
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(value);
    }

    private static bool IsValidHost(string host) =>
        !string.IsNullOrWhiteSpace(host)
        && host.Length <= 253
        && host[0] != '-'
        && !host.Contains('@')
        && !host.Any(char.IsWhiteSpace)
        && (IPAddress.TryParse(host, out _) || Uri.CheckHostName(host) is UriHostNameType.Dns);

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

    public void Dispose() => _httpClient.Dispose();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SshUserPattern();

    [GeneratedRegex("^/tmp/wec-[A-Za-z0-9._-]{1,160}\\.artifact$", RegexOptions.CultureInvariant)]
    private static partial Regex RemotePathPattern();

    [GeneratedRegex("^/tmp/wec-approved-[A-Za-z0-9._+-]{1,128}\\.opsi$", RegexOptions.CultureInvariant)]
    private static partial Regex ApprovedPackagePathPattern();
}
