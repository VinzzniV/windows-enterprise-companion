using System.Security.Cryptography;
using System.Text.Json;

namespace Wec.Modules.PatchManagement.Application;

internal sealed record PatchDashboardSnapshotIdentity(
    string ServerUrl,
    string UserName,
    string? DepotFilter)
{
    public static PatchDashboardSnapshotIdentity From(OpsiSession session, string? depotFilter) => new(
        session.Connection.ServiceUrl.ToString(),
        session.Connection.UserName,
        string.IsNullOrWhiteSpace(depotFilter) ? null : depotFilter);
}

internal sealed class PatchDashboardSnapshotCache
{
    private readonly object _gate = new();
    private string? _identityFingerprint;
    private PatchDashboardResult? _snapshot;

    public void Store(PatchDashboardSnapshotIdentity identity, PatchDashboardResult snapshot)
    {
        lock (_gate)
        {
            _identityFingerprint = Fingerprint(identity);
            _snapshot = snapshot;
        }
    }

    public bool TryGet(PatchDashboardSnapshotIdentity identity, out PatchDashboardResult? snapshot)
    {
        lock (_gate)
        {
            if (_snapshot is not null
                && string.Equals(_identityFingerprint, Fingerprint(identity), StringComparison.Ordinal))
            {
                snapshot = _snapshot;
                return true;
            }

            snapshot = null;
            return false;
        }
    }

    private static string Fingerprint(PatchDashboardSnapshotIdentity identity)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(identity);
        return Convert.ToHexString(SHA256.HashData(json));
    }
}
