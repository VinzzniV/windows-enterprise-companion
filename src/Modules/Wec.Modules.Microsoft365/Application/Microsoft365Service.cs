using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Microsoft365;
using Wec.Core.Results;
using Wec.Modules.Microsoft365.Domain;

namespace Wec.Modules.Microsoft365.Application;

internal sealed class Microsoft365Service(IMicrosoft365Reader reader, IClock clock,
    IOptions<Microsoft365CacheOptions> options) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Microsoft365Query, Microsoft365Snapshot> _cache = [];
    private readonly Dictionary<Microsoft365Resource, Error> _sourceErrors = [];
    private CancellationTokenSource _sessionLifetime = new();
    private long _generation;

    internal async Task<Microsoft365Status> StatusAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Expire();
            Microsoft365Resource[] resources = _cache.Keys.Where(query => query.ObjectId is null).Select(query => query.Resource)
                .Concat(_sourceErrors.Keys).Distinct().ToArray();
            return new(reader.Connection, resources.Select(resource =>
            {
                Microsoft365Snapshot? snapshot = _cache.GetValueOrDefault(new(resource));
                Microsoft365Data? data = snapshot?.Data;
                return new Microsoft365SourceStatus(resource, snapshot?.UpdatedAtUtc,
                    snapshot is null || IsStale(snapshot), data?.Truncated ?? false, data?.TotalCount,
                    data is null ? null : data.Tenants.Count + data.Users.Count + data.Groups.Count + data.Devices.Count
                        + data.ManagedDevices.Count + data.Licenses.Count, _sourceErrors.GetValueOrDefault(resource));
            }).ToArray());
        }
        finally { _gate.Release(); }
    }

    internal async Task<Result<Microsoft365Connection>> ConnectAsync(Microsoft365Configuration configuration, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _cache.Clear();
            _sourceErrors.Clear();
            Interlocked.Increment(ref _generation);
            _sessionLifetime.Dispose();
            _sessionLifetime = new CancellationTokenSource();
            await reader.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            return await reader.ConnectAsync(configuration, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    internal async Task<Result<bool>> DisconnectAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _generation);
        await _sessionLifetime.CancelAsync().ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _cache.Clear();
            _sourceErrors.Clear();
            await reader.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            _sessionLifetime.Dispose();
            _sessionLifetime = new CancellationTokenSource();
            return Result.Success(true);
        }
        finally { _gate.Release(); }
    }

    internal async Task<Result<Microsoft365Snapshot>> ReadAsync(Microsoft365Query query, bool refresh, CancellationToken cancellationToken)
    {
        bool requiresId = query.Resource is not (Microsoft365Resource.Tenant or Microsoft365Resource.Users
            or Microsoft365Resource.Groups or Microsoft365Resource.Devices or Microsoft365Resource.ManagedDevices or Microsoft365Resource.Licenses);
        if (!Enum.IsDefined(query.Resource) || requiresId && query.ObjectId is null
            || query.ObjectId is not null && (!Guid.TryParse(query.ObjectId, out Guid id) || id == Guid.Empty))
        {
            return Result.Failure<Microsoft365Snapshot>(new Error(ErrorCode.InvalidRequest, "Use a known Microsoft 365 resource and a valid object ID."));
        }
        query = query with { ObjectId = query.ObjectId is null ? null : Guid.Parse(query.ObjectId).ToString("D") };
        DateTimeOffset requestedAt = clock.UtcNow;
        long generation = Interlocked.Read(ref _generation);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (generation != Interlocked.Read(ref _generation))
            {
                return Result.Failure<Microsoft365Snapshot>(new Error(ErrorCode.Microsoft365NotConnected, "Microsoft 365 session changed. Reopen the view."));
            }
            Expire();
            _cache.TryGetValue(query, out Microsoft365Snapshot? cached);
            if (cached is not null && (!refresh || cached.UpdatedAtUtc > requestedAt))
            {
                return Result.Success(cached with { Stale = IsStale(cached) });
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _sessionLifetime.Token);
            Result<Microsoft365Data> result = await reader.ReadAsync(query, linked.Token).ConfigureAwait(false);
            if (result.IsFailure)
            {
                if (query.ObjectId is null) { _sourceErrors[query.Resource] = result.Error!; }
                if (cached is null) { return Result.Failure<Microsoft365Snapshot>(result.Error!); }
                Microsoft365Snapshot failed = cached with { Stale = true, RefreshError = result.Error };
                _cache[query] = failed;
                return Result.Success(failed);
            }
            if (generation != Interlocked.Read(ref _generation)) { throw new OperationCanceledException(linked.Token); }
            if (query.ObjectId is null) { _sourceErrors.Remove(query.Resource); }
            var snapshot = new Microsoft365Snapshot(query, result.Value, clock.UtcNow, false, null,
                result.Value.Licenses.Select(license => Capacity(license, options.Value.LicenseWarningRatio)).ToArray());
            if (_cache.Count >= options.Value.MaximumEntries && !_cache.ContainsKey(query))
            {
                _cache.Remove(_cache.MinBy(pair => pair.Value.UpdatedAtUtc).Key);
            }
            _cache[query] = snapshot;
            return Result.Success(snapshot);
        }
        finally { _gate.Release(); }
    }

    internal async Task<Microsoft365Correlation> ContextAsync(string? sid, string? upn, string? host,
        string? entraDeviceId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Expire();
            Microsoft365Snapshot? users = _cache.GetValueOrDefault(new(Microsoft365Resource.Users));
            Microsoft365Snapshot? devices = _cache.GetValueOrDefault(new(Microsoft365Resource.Devices));
            Microsoft365Snapshot? managed = _cache.GetValueOrDefault(new(Microsoft365Resource.ManagedDevices));
            if (entraDeviceId is not null && devices is null)
            {
                devices = _cache.Values.FirstOrDefault(snapshot => snapshot.Query.Resource == Microsoft365Resource.Device
                    && snapshot.Data?.Devices.Any(device => string.Equals(device.DeviceId, entraDeviceId, StringComparison.OrdinalIgnoreCase)) == true);
            }
            bool user = host is null && entraDeviceId is null;
            Microsoft365Snapshot? source = user ? users : devices;
            Microsoft365Correlation context = user
                ? Microsoft365CorrelationPolicy.User(users?.Data?.Users ?? [], sid, upn)
                : Microsoft365CorrelationPolicy.Device(devices?.Data?.Devices ?? [], managed?.Data?.ManagedDevices ?? [], host, entraDeviceId);
            return context with
            {
                ObservedAtUtc = source?.UpdatedAtUtc,
                Stale = source is null || IsStale(source) || !user && (managed is null || IsStale(managed)),
                Explanation = context.Explanation + (source?.Data?.Truncated == true ? " Source inventory is incomplete." : string.Empty),
            };
        }
        finally { _gate.Release(); }
    }

    internal static Microsoft365LicenseCapacity Capacity(Microsoft365License license, double warningRatio)
    {
        bool valid = license.AppliesTo == "User" && license.EnabledSeats is >= 0 && license.ConsumedSeats is >= 0;
        int? remaining = valid ? license.EnabledSeats - license.ConsumedSeats : null;
        return new(license.SkuId, remaining, valid ? license.EnabledSeats > 0
            && (double)license.ConsumedSeats!.Value / license.EnabledSeats.Value >= warningRatio : null,
            valid ? remaining < 0 : null);
    }

    private bool IsStale(Microsoft365Snapshot snapshot) => snapshot.RefreshError is not null
        || clock.UtcNow - snapshot.UpdatedAtUtc >= options.Value.FreshFor;
    private void Expire()
    {
        foreach (Microsoft365Query key in _cache.Where(pair => clock.UtcNow - pair.Value.UpdatedAtUtc >= options.Value.RetainFor)
            .Select(pair => pair.Key).ToArray()) { _cache.Remove(key); }
    }
    public void Dispose() { _gate.Dispose(); _sessionLifetime.Dispose(); }
}
