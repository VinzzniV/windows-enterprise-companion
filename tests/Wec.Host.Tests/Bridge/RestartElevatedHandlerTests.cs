using NSubstitute;
using Wec.Core.Privileges;
using Wec.Core.Results;
using Wec.Host.Bridge;

namespace Wec.Host.Tests.Bridge;

public sealed class RestartElevatedHandlerTests
{
    private readonly IPrivilegeContext _privilegeContext = Substitute.For<IPrivilegeContext>();
    private readonly IElevatedProcessLauncher _launcher = Substitute.For<IElevatedProcessLauncher>();
    private readonly IAppShutdown _appShutdown = Substitute.For<IAppShutdown>();

    private RestartElevatedHandler CreateHandler() =>
        new(_privilegeContext, _launcher, _appShutdown);

    [Fact]
    public async Task AlreadyElevated_IsRejectedWithoutLaunchingAnything()
    {
        _privilegeContext.IsElevated.Returns(true);

        Result<RestartElevatedResult> result = await CreateHandler()
            .HandleAsync(new RestartElevatedRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, result.Error!.Code);
        _launcher.DidNotReceive().TryLaunchElevatedCopy();
        _appShutdown.DidNotReceive().RequestShutdown();
    }

    [Fact]
    public async Task CancelledUacPrompt_IsAValidOutcomeAndKeepsTheAppRunning()
    {
        _privilegeContext.IsElevated.Returns(false);
        _launcher.TryLaunchElevatedCopy().Returns(Result.Success(false));

        Result<RestartElevatedResult> result = await CreateHandler()
            .HandleAsync(new RestartElevatedRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Cancelled);
        _appShutdown.DidNotReceive().RequestShutdown();
    }

    [Fact]
    public async Task SuccessfulLaunch_RequestsShutdownOfTheUnelevatedInstance()
    {
        _privilegeContext.IsElevated.Returns(false);
        _launcher.TryLaunchElevatedCopy().Returns(Result.Success(true));

        Result<RestartElevatedResult> result = await CreateHandler()
            .HandleAsync(new RestartElevatedRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Cancelled);
        _appShutdown.Received(1).RequestShutdown();
    }

    [Fact]
    public async Task LaunchFailure_PropagatesTheErrorAndKeepsTheAppRunning()
    {
        _privilegeContext.IsElevated.Returns(false);
        _launcher.TryLaunchElevatedCopy().Returns(
            Result.Failure<bool>(new Error(ErrorCode.InternalError, "boom")));

        Result<RestartElevatedResult> result = await CreateHandler()
            .HandleAsync(new RestartElevatedRequest(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _appShutdown.DidNotReceive().RequestShutdown();
    }
}
