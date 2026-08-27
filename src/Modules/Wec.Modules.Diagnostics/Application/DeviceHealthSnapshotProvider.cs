using Wec.Core.Contracts;
using Wec.Modules.Diagnostics.Domain;
using Wec.Modules.Diagnostics.Persistence;

namespace Wec.Modules.Diagnostics.Application;

internal sealed class DeviceHealthSnapshotProvider : IDeviceHealthSnapshotProvider
{
    private const int ExpectedCheckCount = 4;

    private readonly IDiagnosticRunRepository _repository;

    public DeviceHealthSnapshotProvider(IDiagnosticRunRepository repository)
    {
        _repository = repository;
    }

    public async Task<DeviceHealthSnapshotData?> GetLatestAsync(
        string? host,
        CancellationToken cancellationToken)
    {
        string cacheKey = host is null
            ? Wec.Core.Targets.ScanTarget.Local.CacheKey
            : Wec.Core.Targets.ScanTarget.Remote(host).CacheKey;
        DiagnosticRunResult? run = await _repository.GetLatestAsync(cacheKey, cancellationToken);
        if (run is null)
        {
            return null;
        }

        IReadOnlyList<DeviceHealthCheckData> checks = [.. run.Results.Select(result =>
            new DeviceHealthCheckData(
                result.DiagnosticId,
                result.Title,
                result.Status.ToString(),
                result.Category.ToString(),
                result.AffectedResource,
                result.CapturedAtUtc))];
        int observedCheckCount = run.Results
            .Select(result => result.DiagnosticId)
            .Distinct(StringComparer.Ordinal)
            .Count();
        bool isComplete = observedCheckCount == ExpectedCheckCount
            && run.Results.All(result => result.Status != DiagnosticStatus.NotRun);

        return new DeviceHealthSnapshotData(
            run.CompletedAtUtc,
            isComplete,
            ExpectedCheckCount,
            observedCheckCount,
            checks);
    }
}
