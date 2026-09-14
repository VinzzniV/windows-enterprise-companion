using Wec.Core.Results;

namespace Wec.Core.Contracts;

public enum StoredDeviceListSource { Inventory, Security, SavedClients }
public sealed record StoredDeviceAddressRow(string RecordId, string Host, string Label, DateTimeOffset ObservedAtUtc);
public sealed record StoredDeviceAddressPage(int TotalRecords, IReadOnlyList<StoredDeviceAddressRow> Records);

public interface IStoredDeviceListProvider
{
    StoredDeviceListSource Source { get; }
    Task<Result<StoredDeviceAddressPage>> ReadAsync(int maximumRecords, string? search, CancellationToken cancellationToken);
}
