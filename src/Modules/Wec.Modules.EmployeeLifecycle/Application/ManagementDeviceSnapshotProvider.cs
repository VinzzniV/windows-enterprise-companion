using Wec.Core.Contracts;

namespace Wec.Modules.EmployeeLifecycle.Application;

internal sealed class ManagementDeviceSnapshotProvider(ItHygieneSnapshotCache cache) : IManagementDeviceSnapshotProvider
{
    public Task<ManagementDeviceSnapshot?> ReadCachedAsync(
        DirectoryInventoryConnection? activeDirectory,
        KasperskyInventoryConnection? kaspersky,
        CancellationToken cancellationToken) =>
        cache.ReadCachedAsync(new ItHygieneRequest(activeDirectory, kaspersky), cancellationToken);

    internal static ManagementDeviceSnapshot Project(
        HygieneSourceLoad load, DateTimeOffset retrievedAtUtc, ItHygieneRequest request, ItLifecycleOptions options)
    {
        IReadOnlyList<AdComputerInventoryItem> ad = load.ActiveDirectory.IsSuccess ? load.ActiveDirectory.Value.Computers : [];
        KasperskyDeviceRecord[] ksc = load.Kaspersky.IsSuccess
            ? load.Kaspersky.Value.Computers.Select(item => new KasperskyDeviceRecord(
                item.ComputerName, item.Fqdn, item.DnsName, item.RecordName,
                item.LastSeen, item.AgentVersion, item.KesVersion, item.AdministrationGroup)).ToArray()
            : [];
        IReadOnlyList<OpsiComputerInventoryItem> opsi = load.Opsi.IsSuccess ? load.Opsi.Value.Computers : [];
        IReadOnlyList<NessusComputerInventoryItem> nessus = load.Nessus.IsSuccess ? load.Nessus.Value.Computers : [];
        return new(retrievedAtUtc,
        [
            State("ActiveDirectory", load.DomainName, load.States.ActiveDirectory, ad.Count),
            State("Kaspersky", EndpointScope(request.Kaspersky?.Server ?? options.Kaspersky.Server), load.States.Kaspersky, ksc.Length),
            State("Opsi", load.Opsi.IsSuccess ? load.Opsi.Value.SourceScope : null, load.States.Opsi, opsi.Count),
            State("Nessus", null, load.States.Nessus, nessus.Count),
        ], ad, ksc, opsi, nessus);
    }

    private static ManagementDeviceSourceState State(string source, string? scope, InventorySourceState state, int count) =>
        new(source, scope, state.Availability.ToString(), state.Error, count);

    private static string? EndpointScope(string? server)
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            return null;
        }
        return Uri.TryCreate(server.Contains("://", StringComparison.Ordinal) ? server : $"https://{server}",
            UriKind.Absolute, out Uri? uri) ? uri.Authority : null;
    }
}
