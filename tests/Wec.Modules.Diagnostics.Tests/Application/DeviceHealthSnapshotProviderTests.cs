using NSubstitute;
using Wec.Modules.Diagnostics.Application;
using Wec.Modules.Diagnostics.Domain;
using Wec.Modules.Diagnostics.Persistence;

namespace Wec.Modules.Diagnostics.Tests.Application;

public sealed class DeviceHealthSnapshotProviderTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);
    private readonly IDiagnosticRunRepository _repository = Substitute.For<IDiagnosticRunRepository>();

    [Fact]
    public async Task CompleteStoredRun_MapsToCoreProjection()
    {
        _repository.GetLatestAsync(
                Wec.Core.Targets.ScanTarget.Remote("PC-42").CacheKey,
                Arg.Any<CancellationToken>())
            .Returns(new DiagnosticRunResult(
                CapturedAt.AddMinutes(-1),
                CapturedAt,
                [
                    Result(DeviceHealthDiagnosticIds.WindowsUpdateRecency, DiagnosticStatus.Pass),
                    Result(DeviceHealthDiagnosticIds.ServiceStatus, DiagnosticStatus.Warning),
                    Result(DeviceHealthDiagnosticIds.EventLogSummary, DiagnosticStatus.Pass),
                    Result(DeviceHealthDiagnosticIds.DiskFreeSpace, DiagnosticStatus.Pass),
                ]));
        var provider = new DeviceHealthSnapshotProvider(_repository);

        Wec.Core.Contracts.DeviceHealthSnapshotData? snapshot = await provider.GetLatestAsync(
            "PC-42",
            CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.True(snapshot.IsComplete);
        Assert.Equal(4, snapshot.ExpectedCheckCount);
        Assert.Equal(4, snapshot.ObservedCheckCount);
        Assert.Contains(snapshot.Checks, check =>
            check.DiagnosticId == DeviceHealthDiagnosticIds.ServiceStatus
            && check.Status == nameof(DiagnosticStatus.Warning));
    }

    [Fact]
    public async Task MissingOrNotRunCheck_MarksCoverageIncomplete()
    {
        _repository.GetLatestAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DiagnosticRunResult(
                CapturedAt.AddMinutes(-1),
                CapturedAt,
                [
                    Result(DeviceHealthDiagnosticIds.WindowsUpdateRecency, DiagnosticStatus.Pass),
                    Result(DeviceHealthDiagnosticIds.ServiceStatus, DiagnosticStatus.NotRun),
                    Result(DeviceHealthDiagnosticIds.DiskFreeSpace, DiagnosticStatus.Pass),
                ]));
        var provider = new DeviceHealthSnapshotProvider(_repository);

        Wec.Core.Contracts.DeviceHealthSnapshotData? snapshot = await provider.GetLatestAsync(
            host: null,
            CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.False(snapshot.IsComplete);
        Assert.Equal(3, snapshot.ObservedCheckCount);
        await _repository.Received(1).GetLatestAsync(
            Wec.Core.Targets.ScanTarget.Local.CacheKey,
            Arg.Any<CancellationToken>());
    }

    private static DiagnosticResult Result(string id, DiagnosticStatus status) => new(
        id,
        id,
        status,
        DiagnosticCategory.System,
        id,
        new Dictionary<string, string>(),
        [],
        RequiredPrivilege: null,
        CapturedAt);
}
