using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.Diagnostics.Application;
using Wec.Modules.Diagnostics.Application.Diagnostics;
using Wec.Modules.Diagnostics.Domain;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Wec.Modules.Diagnostics.Tests.Application;

internal static class DiagnosticsTestSetup
{
    public static readonly DateTimeOffset Now = new(2026, 7, 3, 9, 0, 0, TimeSpan.Zero);

    public static IClock Clock()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return clock;
    }

    public static Microsoft.Extensions.Options.IOptions<DiagnosticsOptions> Options() =>
        MsOptions.Create(new DiagnosticsOptions());
}

public class DiskFreeSpaceDiagnosticTests
{
    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();

    private DiskFreeSpaceDiagnostic CreateDiagnostic() => new(
        _wmiQueryService, DiagnosticsTestSetup.Options(), DiagnosticsTestSetup.Clock());

    private void SetUpLogicalDisks(params (string Name, ulong Total, ulong Free)[] disks) =>
        _wmiQueryService.QueryAsync(
                Arg.Any<Wec.Core.Targets.ScanTarget>(),
                Arg.Any<Wec.Core.Targets.ScanCredentials>(),
                Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Is<string>(query => query.Contains("Win32_LogicalDisk", StringComparison.Ordinal)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<WmiInstance>>([.. disks.Select(disk =>
                new WmiInstance(new Dictionary<string, object?>
                {
                    ["DeviceID"] = disk.Name,
                    ["Size"] = disk.Total,
                    ["FreeSpace"] = disk.Free,
                }))]));

    [Fact]
    public async Task DriveBelowThreshold_ProducesWarning()
    {
        SetUpLogicalDisks(("C:", 100_000_000_000, 5_000_000_000), ("D:", 100_000_000_000, 50_000_000_000));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal("WEC-DIAG-SYS-DISKSPACE", result.DiagnosticId);
        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.Contains("C:", result.Title, StringComparison.Ordinal);
        Assert.Equal(DiagnosticCategory.System, result.Category);
    }

    [Fact]
    public async Task AllDrivesAboveThreshold_ProducesPass()
    {
        SetUpLogicalDisks(("C:", 100_000_000_000, 40_000_000_000));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Pass, result.Status);
    }

    [Fact]
    public async Task RemoteTarget_QueriesTheRemoteMachine()
    {
        var remoteContext = new DiagnosticContext(
            Wec.Core.Targets.ScanTarget.Remote("pc-042"),
            Wec.Core.Targets.ScanCredentials.CurrentUser,
            Wec.Core.Targets.ConnectionOptions.Default);
        SetUpLogicalDisks(("C:", 100_000_000_000, 40_000_000_000));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(remoteContext, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Pass, result.Status);
        await _wmiQueryService.Received(1).QueryAsync(
            Arg.Is<Wec.Core.Targets.ScanTarget>(target => target.Host == "pc-042"),
            Arg.Any<Wec.Core.Targets.ScanCredentials>(),
            Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }
}

public class WindowsUpdateRecencyDiagnosticTests
{
    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();

    private WindowsUpdateRecencyDiagnostic CreateDiagnostic(int maximumAgeInDays = 60) => new(
        _wmiQueryService,
        MsOptions.Create(new DiagnosticsOptions
        {
            MaxDaysSinceLastInstalledUpdate = maximumAgeInDays,
        }),
        DiagnosticsTestSetup.Clock());

    private void SetUpHotfixes(params (string Id, string? InstalledOn)[] hotfixes) =>
        _wmiQueryService.QueryAsync(
                Arg.Any<Wec.Core.Targets.ScanTarget>(),
                Arg.Any<Wec.Core.Targets.ScanCredentials>(),
                Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Is<string>(query => query.Contains("Win32_QuickFixEngineering", StringComparison.Ordinal)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<WmiInstance>>([.. hotfixes.Select(hotfix =>
                new WmiInstance(new Dictionary<string, object?>
                {
                    ["HotFixID"] = hotfix.Id,
                    ["InstalledOn"] = hotfix.InstalledOn,
                }))]));

    [Fact]
    public async Task RecentUpdate_ProducesPassWithStableContractEvidence()
    {
        SetUpHotfixes(("KB5070001", "2026-06-15"), ("KB5060001", "2026-01-10"));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal("WEC-DIAG-SYS-UPDATES", result.DiagnosticId);
        Assert.Equal(DiagnosticStatus.Pass, result.Status);
        Assert.Equal(DiagnosticCategory.System, result.Category);
        Assert.Equal("KB5070001", result.Evidence["hotfix"]);
        Assert.Equal("2026-06-15", result.Evidence["lastInstalledUpdate"]);
        Assert.Equal("18", result.Evidence["daysSinceLastUpdate"]);
    }

    [Fact]
    public async Task UpdateOlderThanThreshold_ProducesWarning()
    {
        SetUpHotfixes(("KB5060001", "2026-01-10"));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic(maximumAgeInDays: 60)
                .EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Warning, result.Status);
        Assert.NotEmpty(result.SuggestedNextSteps);
    }

    [Fact]
    public async Task MissingParseableDates_ProducesNotRun()
    {
        SetUpHotfixes(("KB5070001", null), ("KB5060001", "not-a-date"));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.NotRun, result.Status);
        Assert.Equal("2", result.Evidence["hotfixCount"]);
    }

    [Fact]
    public async Task WmiFailure_ProducesNotRun()
    {
        _wmiQueryService.QueryAsync(
                Arg.Any<Wec.Core.Targets.ScanTarget>(),
                Arg.Any<Wec.Core.Targets.ScanCredentials>(),
                Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<WmiInstance>>(Error.WmiUnavailable("unreachable")));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(DiagnosticContext.Local, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.NotRun, result.Status);
        Assert.Equal("unreachable", result.Evidence["errorMessage"]);
    }

    [Fact]
    public async Task RemoteTarget_QueriesTheRemoteMachine()
    {
        var remoteContext = new DiagnosticContext(
            Wec.Core.Targets.ScanTarget.Remote("pc-042"),
            Wec.Core.Targets.ScanCredentials.CurrentUser,
            Wec.Core.Targets.ConnectionOptions.Default);
        SetUpHotfixes(("KB5070001", "2026-06-15"));

        DiagnosticResult result = Assert.Single(
            await CreateDiagnostic().EvaluateAsync(remoteContext, CancellationToken.None));

        Assert.Equal(DiagnosticStatus.Pass, result.Status);
        await _wmiQueryService.Received(1).QueryAsync(
            Arg.Is<Wec.Core.Targets.ScanTarget>(target => target.Host == "pc-042"),
            Arg.Any<Wec.Core.Targets.ScanCredentials>(),
            Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }
}
