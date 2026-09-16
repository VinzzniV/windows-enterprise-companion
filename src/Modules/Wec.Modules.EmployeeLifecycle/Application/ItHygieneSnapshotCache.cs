using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Wec.Core.Results;
using Wec.Core.Contracts;

namespace Wec.Modules.EmployeeLifecycle.Application;

internal sealed class ItHygieneSnapshotCache(IServiceCredentialStore credentials,
    IOpsiComputerInventoryProvider opsi, IOptions<ItLifecycleOptions> options) : IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private string? _requestFingerprint;
    private ItHygieneResult? _snapshot;
    private ManagementDeviceSnapshot? _sourceSnapshot;
    private Result<ItHygieneResult>? _lastResult;
    private long _session;
    private long _revision;

    internal Task<ManagementDeviceSnapshot?> ReadCachedAsync(ItHygieneRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fingerprint = Fingerprint(request);
        lock (_gate)
        {
            Activate(fingerprint);
            SynchronizeOpsiSession();
            return Task.FromResult(_sourceSnapshot);
        }
    }

    public async Task<Result<ItHygieneResult>> GetAsync(ItHygieneRequest request, bool force,
        Func<ItHygieneRequest, CancellationToken, Task<Result<ItHygieneResult>>> load, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fingerprint = Fingerprint(request);
        long session;
        long revision;
        lock (_gate)
        {
            Activate(fingerprint);
            SynchronizeOpsiSession();
            session = _session;
            revision = _revision;
            if (!force && _snapshot is not null) { return Result.Success(_snapshot); }
        }
        await _readGate.WaitAsync(cancellationToken);
        try
        {
            lock (_gate)
            {
                if (session != _session) { return Changed(); }
                SynchronizeOpsiSession();
                if (_revision > revision && _lastResult is not null) { return _lastResult; }
                if (!force && _snapshot is not null) { return Result.Success(_snapshot); }
            }
            Result<ItHygieneResult> loaded = await load(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            string completedFingerprint = Fingerprint(request);
            lock (_gate)
            {
                if (session != _session) { return Changed(); }
                if (completedFingerprint != fingerprint) { Activate(completedFingerprint); return Changed(); }
                ++_revision;
                if (loaded.IsSuccess)
                {
                    _sourceSnapshot = loaded.Value.SourceRecords is { } sources
                        ? sources with { SnapshotId = Guid.NewGuid(), SessionRevision = _session, Revision = _revision } : null;
                    loaded = Result.Success(loaded.Value with { SourceRecords = _sourceSnapshot });
                    if (Cacheable(loaded.Value)) { _snapshot = loaded.Value; }
                }
                _lastResult = loaded;
                SynchronizeOpsiSession();
                return _lastResult ?? loaded;
            }
        }
        finally { _readGate.Release(); }
    }

    private void SynchronizeOpsiSession()
    {
        Guid? currentSession = opsi.CurrentSessionId;
        if (_sourceSnapshot is null || _sourceSnapshot.OpsiSessionId == currentSession) { return; }
        const string explanation = "The opsi session changed. Read its inventory again in the selected session.";
        _sourceSnapshot = _sourceSnapshot with
        {
            Opsi = [], OpsiSessionId = currentSession, Revision = ++_revision,
            Sources = _sourceSnapshot.Sources.Select(state => state.Source != "Opsi" ? state
                : state with { Scope = null, Availability = currentSession is null ? "NotConnected" : "NotLoaded",
                    LoadedRecords = 0, Error = explanation }).ToArray(),
        };
        ItHygieneResult? previous = _snapshot ?? (_lastResult is { IsSuccess: true } ? _lastResult.Value : null);
        if (previous is null) { return; }
        EnvironmentSourceStates states = previous.Sources with
        {
            Opsi = new(currentSession is null ? InventorySourceAvailability.NotConnected : InventorySourceAvailability.Partial, explanation),
        };
        IReadOnlyList<HygieneDevice> devices = ItHygieneService.CorrelateAndAssess(
            _sourceSnapshot.ActiveDirectory,
            _sourceSnapshot.Kaspersky.Select(item => new KasperskyComputer(item.ComputerName, item.LastSeen,
                item.AgentVersion, item.KesVersion, item.AdministrationGroup, item.Fqdn, item.DnsName, item.RecordName)).ToArray(),
            [], previous.NessusInventory ?? new(_sourceSnapshot.Nessus, NessusInventoryAvailability.Partial, null),
            states, previous.AssessedAtUtc, options.Value);
        ItHygieneResult sanitized = previous with { Sources = states, Devices = devices,
            Summary = ItHygieneService.Summarize(devices), SourceRecords = _sourceSnapshot };
        if (_snapshot is not null) { _snapshot = sanitized; }
        _lastResult = Result.Success(sanitized);
    }

    private void Activate(string fingerprint)
    {
        if (_requestFingerprint == fingerprint) { return; }
        _requestFingerprint = fingerprint;
        _snapshot = null;
        _sourceSnapshot = null;
        _lastResult = null;
        ++_session;
        ++_revision;
    }
    private static Result<ItHygieneResult> Changed() => Result.Failure<ItHygieneResult>(new(ErrorCode.DirectoryUnavailable,
        "The management connection context changed. Read the sources again in the selected context."));
    private static bool Cacheable(ItHygieneResult result) => result.Sources.Kaspersky.Availability != InventorySourceAvailability.Unavailable;
    private string Fingerprint(ItHygieneRequest request)
    {
        Result<StoredServiceCredential?>? stored = !string.IsNullOrWhiteSpace(request.Kaspersky?.UserName) && request.Kaspersky.Password is not null
            ? null : credentials.Read(ServiceCredentialKind.Kaspersky);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new { request.ActiveDirectory, request.Kaspersky,
            StoredKaspersky = stored is { IsSuccess: true } ? stored.Value : null, StoredCredentialError = stored?.Error?.Code });
        try { return Convert.ToHexString(SHA256.HashData(json)); }
        finally { CryptographicOperations.ZeroMemory(json); }
    }
    public void Dispose() => _readGate.Dispose();
}
