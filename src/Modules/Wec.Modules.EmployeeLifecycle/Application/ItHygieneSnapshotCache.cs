using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wec.Core.Results;
using Wec.Core.Contracts;

namespace Wec.Modules.EmployeeLifecycle.Application;

internal sealed class ItHygieneSnapshotCache : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _requestFingerprint;
    private ItHygieneResult? _snapshot;

    internal async Task<ManagementDeviceSnapshot?> ReadCachedAsync(ItHygieneRequest request, CancellationToken cancellationToken)
    {
        string fingerprint = Fingerprint(request);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return string.Equals(_requestFingerprint, fingerprint, StringComparison.Ordinal)
                ? _snapshot?.SourceRecords
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<ItHygieneResult>> GetAsync(
        ItHygieneRequest request,
        bool force,
        Func<ItHygieneRequest, CancellationToken, Task<Result<ItHygieneResult>>> load,
        CancellationToken cancellationToken)
    {
        string fingerprint = Fingerprint(request);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!force && _snapshot is not null && string.Equals(_requestFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return Result.Success(_snapshot);
            }

            if (!string.Equals(_requestFingerprint, fingerprint, StringComparison.Ordinal))
            {
                _snapshot = null;
                _requestFingerprint = fingerprint;
            }
            Result<ItHygieneResult> loaded = await load(request, cancellationToken);
            if (loaded.IsSuccess && Cacheable(loaded.Value))
            {
                _requestFingerprint = fingerprint;
                _snapshot = loaded.Value;
            }

            return loaded;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private static bool Cacheable(ItHygieneResult result) =>
        result.Sources.Kaspersky.Availability != InventorySourceAvailability.Unavailable;

    private static string Fingerprint(ItHygieneRequest request)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            request.ActiveDirectory,
            request.Kaspersky,
        });
        return Convert.ToHexString(SHA256.HashData(json));
    }
}
