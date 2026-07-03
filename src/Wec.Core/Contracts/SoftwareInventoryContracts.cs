namespace Wec.Core.Contracts;

public sealed record InstalledSoftwareRecordData(string Name, string? Version, string? Publisher);

public sealed record HostInstalledSoftwareData(
    string Host,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<InstalledSoftwareRecordData> Software);

/// <summary>
/// Implemented by the Inventory module; consumed via Core only (ADR 0004).
/// Feeds the Patch Management comparison between inventoried software and
/// opsi products (ADR 0008).
/// </summary>
public interface IInstalledSoftwareInventoryProvider
{
    /// <summary>Stored hosts whose latest snapshot captured a software list.</summary>
    Task<IReadOnlyList<HostInstalledSoftwareData>> GetAllHostsAsync(CancellationToken cancellationToken);
}
