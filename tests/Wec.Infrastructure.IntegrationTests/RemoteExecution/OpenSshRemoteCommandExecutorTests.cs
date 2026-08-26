using Wec.Core.RemoteExecution;
using Wec.Core.Results;
using Wec.Infrastructure.RemoteExecution;

namespace Wec.Infrastructure.IntegrationTests.RemoteExecution;

public sealed class OpenSshRemoteCommandExecutorTests
{
    [Fact]
    public void FileUploader_RejectsRemotePathsOutsideDedicatedTmpPrefix()
    {
        string file = Path.GetTempFileName();
        try
        {
            Result<bool> result = OpenSshRemoteFileUploader.Validate(new RemoteFileUploadRequest(
                file, "depot01.example.test", "root", "/var/lib/opsi/workbench/setup.tar.gz",
                TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1)));

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorCode.InvalidRequest, result.Error!.Code);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void FileUploader_RejectsNonPositiveTimeouts()
    {
        string file = Path.GetTempFileName();
        try
        {
            Result<bool> result = OpenSshRemoteFileUploader.Validate(new RemoteFileUploadRequest(
                file, "depot01.example.test", "root",
                $"/tmp/wec-winget-{Guid.NewGuid():N}.tar.gz",
                TimeSpan.Zero, TimeSpan.FromMinutes(1)));

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorCode.InvalidRequest, result.Error!.Code);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void BuildStartInfo_UsesNonInteractiveStrictArgumentsWithoutShell()
    {
        var request = new RemoteCommandRequest(
            "depot01.example.test",
            "opsiadmin",
            "sudo -n opsi-package-updater -v update firefox",
            TimeSpan.FromSeconds(7),
            TimeSpan.FromMinutes(5));

        Result<System.Diagnostics.ProcessStartInfo> result =
            OpenSshRemoteCommandExecutor.BuildStartInfo(request);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.UseShellExecute);
        Assert.True(result.Value.RedirectStandardOutput);
        Assert.Equal(
        [
            "-o", "BatchMode=yes",
            "-o", "StrictHostKeyChecking=yes",
            "-o", "LogLevel=ERROR",
            "-o", "ConnectTimeout=7",
            "opsiadmin@depot01.example.test",
            "sudo -n opsi-package-updater -v update firefox",
        ],
            result.Value.ArgumentList);
    }

    [Theory]
    [InlineData("-oProxyCommand=bad")]
    [InlineData("depot host")]
    [InlineData("user@host")]
    public void BuildStartInfo_RejectsUnsafeHost(string host)
    {
        Result<System.Diagnostics.ProcessStartInfo> result =
            OpenSshRemoteCommandExecutor.BuildStartInfo(new RemoteCommandRequest(
                host,
                "root",
                "opsi-package-updater -v update firefox",
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMinutes(1)));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, result.Error!.Code);
    }

    [Fact]
    public void BuildStartInfo_RejectsCommandLineBreaks()
    {
        Result<System.Diagnostics.ProcessStartInfo> result =
            OpenSshRemoteCommandExecutor.BuildStartInfo(new RemoteCommandRequest(
                "depot01.example.test",
                "root",
                "safe\nsecond-command",
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMinutes(1)));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, result.Error!.Code);
    }
}
