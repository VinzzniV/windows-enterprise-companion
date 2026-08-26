namespace Wec.Core.Contracts;

public sealed record DeviceHealthCheckData(
    string DiagnosticId,
    string Title,
    string Status,
    string Category,
    string AffectedResource,
    DateTimeOffset CapturedAtUtc);

public sealed record DeviceHealthSnapshotData(
    DateTimeOffset CompletedAtUtc,
    bool IsComplete,
    int ExpectedCheckCount,
    int ObservedCheckCount,
    IReadOnlyList<DeviceHealthCheckData> Checks);

/// <summary>Implemented by Diagnostics; exposes only the latest stored Health projection.</summary>
public interface IDeviceHealthSnapshotProvider
{
    /// <param name="host">null = the local machine; otherwise the scanned remote host.</param>
    Task<DeviceHealthSnapshotData?> GetLatestAsync(string? host, CancellationToken cancellationToken);
}
