using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Application;
using Wec.Modules.Diagnostics.Domain;

namespace Wec.Modules.Diagnostics.Tests.Application;

internal static class TestDefaults
{
    public static readonly DateTimeOffset Now = new(2026, 7, 2, 17, 0, 0, TimeSpan.Zero);

    public static IClock Clock()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return clock;
    }
}

public class DiagnosticRunServiceTests
{
    [Fact]
    public async Task CrashingDiagnostic_BecomesVisibleFailResult_AndOthersStillRun()
    {
        var crashing = Substitute.For<IDiagnostic>();
        crashing.DiagnosticId.Returns("CRASHING");
        crashing.EvaluateAsync(Arg.Any<DiagnosticContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("bug"));

        var healthy = Substitute.For<IDiagnostic>();
        healthy.DiagnosticId.Returns("HEALTHY");
        healthy.EvaluateAsync(Arg.Any<DiagnosticContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DiagnosticResult>>([new DiagnosticResult(
                "HEALTHY", "ok", DiagnosticStatus.Pass, DiagnosticCategory.System, "x",
                new Dictionary<string, string>(), [], null, TestDefaults.Now)]));

        var service = new DiagnosticRunService(
            [crashing, healthy], TestDefaults.Clock(), NullLogger<DiagnosticRunService>.Instance);

        Result<DiagnosticRunResult> run = await service.RunAsync(DiagnosticContext.Local, CancellationToken.None);

        Assert.True(run.IsSuccess);
        Assert.Equal(2, run.Value.Results.Count);
        Assert.Contains(run.Value.Results, result =>
            result.DiagnosticId == "CRASHING" && result.Status == DiagnosticStatus.Fail);
        Assert.Contains(run.Value.Results, result =>
            result.DiagnosticId == "HEALTHY" && result.Status == DiagnosticStatus.Pass);
    }

    [Fact]
    public async Task Results_AreOrderedFailWarningNotRunPass()
    {
        var diagnostic = Substitute.For<IDiagnostic>();
        diagnostic.DiagnosticId.Returns("ORDER");
        diagnostic.EvaluateAsync(Arg.Any<DiagnosticContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DiagnosticResult>>
            ([
                Result("pass", DiagnosticStatus.Pass),
                Result("not-run", DiagnosticStatus.NotRun),
                Result("warning", DiagnosticStatus.Warning),
                Result("fail", DiagnosticStatus.Fail),
            ]));
        var service = new DiagnosticRunService(
            [diagnostic], TestDefaults.Clock(), NullLogger<DiagnosticRunService>.Instance);

        Result<DiagnosticRunResult> run = await service.RunAsync(DiagnosticContext.Local, CancellationToken.None);

        Assert.Equal(
            [DiagnosticStatus.Fail, DiagnosticStatus.Warning, DiagnosticStatus.NotRun, DiagnosticStatus.Pass],
            run.Value.Results.Select(result => result.Status));
    }

    private static DiagnosticResult Result(string id, DiagnosticStatus status) => new(
        id, id, status, DiagnosticCategory.System, id,
        new Dictionary<string, string>(), [], null, TestDefaults.Now);
}
