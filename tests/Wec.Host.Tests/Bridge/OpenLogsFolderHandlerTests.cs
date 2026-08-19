using System.IO;
using OptionsFactory = Microsoft.Extensions.Options.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Host.Bridge;
using Wec.Infrastructure.Logging;

namespace Wec.Host.Tests.Bridge;

public sealed class OpenLogsFolderHandlerTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"wec-logs-{Guid.NewGuid():N}");

    [Fact]
    public async Task UsesSharedShellLauncherForValidatedLogDirectory()
    {
        Directory.CreateDirectory(_directory);
        var shell = Substitute.For<IShellLauncher>();
        shell.TryOpenPath(_directory).Returns(true);
        var handler = new OpenLogsFolderHandler(
            OptionsFactory.Create(new LoggingOptions { LogDirectory = _directory }),
            shell);

        Result<OpenLogsFolderResponse> result = await handler.HandleAsync(
            new OpenLogsFolderRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_directory, result.Value.LogDirectory);
        shell.Received(1).TryOpenPath(_directory);
    }

    [Fact]
    public async Task ShellFailure_IsReturnedToTheUi()
    {
        Directory.CreateDirectory(_directory);
        var shell = Substitute.For<IShellLauncher>();
        shell.TryOpenPath(_directory).Returns(false);
        var handler = new OpenLogsFolderHandler(
            OptionsFactory.Create(new LoggingOptions { LogDirectory = _directory }),
            shell);

        Result<OpenLogsFolderResponse> result = await handler.HandleAsync(
            new OpenLogsFolderRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.InternalError, result.Error!.Code);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory);
        }
    }
}
