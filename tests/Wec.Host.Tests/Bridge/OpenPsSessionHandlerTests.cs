using NSubstitute;
using Wec.Core.Results;
using Wec.Host.Bridge;

namespace Wec.Host.Tests.Bridge;

public sealed class OpenPsSessionHandlerTests
{
    private readonly IPowerShellSessionLauncher _launcher = Substitute.For<IPowerShellSessionLauncher>();

    private OpenPsSessionHandler CreateHandler() => new(_launcher);

    [Fact]
    public async Task HandleAsync_ValidHost_LaunchesWithTheGivenSpec()
    {
        _launcher.Launch(Arg.Any<PowerShellSessionSpec>()).Returns(Result.Success(true));

        Result<OpenPsSessionResponse> result = await CreateHandler().HandleAsync(
            new OpenPsSessionRequest("PC-1", "admin", "CORP", "secret"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Launched);
        _launcher.Received(1).Launch(Arg.Is<PowerShellSessionSpec>(spec =>
            spec.Host == "PC-1" && spec.UserName == "admin" && spec.Domain == "CORP" && spec.Password == "secret"));
    }

    [Fact]
    public async Task HandleAsync_BlankHost_FailsWithoutLaunching()
    {
        Result<OpenPsSessionResponse> result = await CreateHandler().HandleAsync(
            new OpenPsSessionRequest("   ", null, null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, result.Error!.Code);
        _launcher.DidNotReceive().Launch(Arg.Any<PowerShellSessionSpec>());
    }

    [Fact]
    public async Task HandleAsync_LauncherFailure_PropagatesError()
    {
        _launcher.Launch(Arg.Any<PowerShellSessionSpec>())
            .Returns(Result.Failure<bool>(new Error(ErrorCode.InternalError, "boom")));

        Result<OpenPsSessionResponse> result = await CreateHandler().HandleAsync(
            new OpenPsSessionRequest("PC-1", null, null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.InternalError, result.Error!.Code);
    }
}
