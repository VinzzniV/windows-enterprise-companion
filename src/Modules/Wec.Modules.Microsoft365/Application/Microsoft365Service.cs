using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Microsoft365;
using Wec.Core.Results;
using Wec.Core.Objects;
using Wec.Modules.Microsoft365.Domain;

namespace Wec.Modules.Microsoft365.Application;

internal sealed class Microsoft365Service(IMicrosoft365Reader reader, IClock clock,
    IOptions<Microsoft365CacheOptions> options) : IMicrosoft365DeviceContextProvider, IMicrosoft365UserContextProvider, IMicrosoft365GroupContextProvider, IMicrosoft365ObjectListProvider, IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private readonly Dictionary<Microsoft365Query, CacheEntry> _cache = [];
    private CancellationTokenSource _sessionLifetime = new();
    private long _generation;
    private long _revision;
    private bool _sessionChanging;
    private bool _sessionReady = true;
    private Microsoft365Query? _activeQuery;
    private DateTimeOffset? _activeAttemptAtUtc;

    private sealed record CacheEntry(Microsoft365Snapshot? Snapshot, DateTimeOffset LastAttemptAtUtc,
        Error? Error, long Revision);

    internal Task<Microsoft365Status> StatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Expire();
            Microsoft365SourceStatus[] sources = _cache.Where(pair => pair.Key.ObjectId is null && pair.Key.SecurityIdentifier is null).Select(pair =>
            {
                Microsoft365ReadState state = State(pair.Key);
                return new Microsoft365SourceStatus(pair.Key.Resource, state.RetrievedAtUtc,
                    state.Freshness != EvidenceFreshness.Fresh, state.Coverage == EvidenceCoverage.Partial,
                    state.DeclaredTotal, state.LoadedCount, state.LastAttemptError);
            }).ToArray();
            Microsoft365Query[] queries = _cache.Keys.Concat(_activeQuery is null ? [] : new[] { _activeQuery }).Distinct().ToArray();
            return Task.FromResult(new Microsoft365Status(Connection, sources)
            {
                SessionRevision = _generation, Revision = _revision,
                Queries = queries.Select(State).ToArray(),
            });
        }
    }

    internal Task<Result<Microsoft365Connection>> ConnectAsync(Microsoft365Configuration configuration, CancellationToken cancellationToken) =>
        ChangeSessionAsync(configuration, cancellationToken);

    internal async Task<Result<bool>> DisconnectAsync(CancellationToken cancellationToken)
    {
        Result<Microsoft365Connection> result = await ChangeSessionAsync(null, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Result.Success(true) : Result.Failure<bool>(result.Error!);
    }

    private async Task<Result<Microsoft365Connection>> ChangeSessionAsync(Microsoft365Configuration? configuration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CancellationTokenSource previous;
        long generation;
        lock (_gate)
        {
            previous = _sessionLifetime;
            _sessionLifetime = new CancellationTokenSource();
            generation = ++_generation;
            ++_revision;
            _cache.Clear();
            _activeQuery = null;
            _activeAttemptAtUtc = null;
            _sessionChanging = true;
            _sessionReady = false;
        }
        await previous.CancelAsync().ConfigureAwait(false);
        previous.Dispose();
        bool acquired = false;
        try
        {
            await _readGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            CancellationTokenSource linked;
            lock (_gate)
            {
                if (generation != _generation) { return SessionChanged<Microsoft365Connection>(); }
                linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _sessionLifetime.Token);
            }
            using (linked)
            {
                await reader.DisconnectAsync(linked.Token).ConfigureAwait(false);
                Result<Microsoft365Connection> result = configuration is null
                    ? Result.Success(reader.Connection)
                    : await reader.ConnectAsync(configuration, linked.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    if (generation == _generation) { _sessionReady = result.IsSuccess; }
                    return generation == _generation ? result : SessionChanged<Microsoft365Connection>();
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                if (generation == _generation) { _sessionChanging = false; ++_revision; }
            }
            if (acquired) { _readGate.Release(); }
        }
    }

    internal async Task<Result<Microsoft365Snapshot>> ReadAsync(Microsoft365Query query, bool refresh, CancellationToken cancellationToken,
        string? expectedTenantId = null)
    {
        Result<Microsoft365Query> normalized = Microsoft365QueryValidation.Normalize(query);
        if (normalized.IsFailure) { return Result.Failure<Microsoft365Snapshot>(normalized.Error!); }
        query = normalized.Value;
        cancellationToken.ThrowIfCancellationRequested();
        long generation;
        long requestedRevision;
        lock (_gate)
        {
            if (_sessionChanging || !_sessionReady) { return SessionChanged<Microsoft365Snapshot>(); }
            if (expectedTenantId is not null && (!ValidId(expectedTenantId) || !ValidId(Connection.Configuration.TenantId)
                || Guid.Parse(expectedTenantId) != Guid.Parse(Connection.Configuration.TenantId)))
            {
                return SessionChanged<Microsoft365Snapshot>();
            }
            Expire();
            generation = _generation;
            requestedRevision = _revision;
            if (!refresh && _cache.GetValueOrDefault(query)?.Snapshot is not null) { return CachedResult(query); }
        }
        await _readGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CancellationTokenSource linked;
            lock (_gate)
            {
                if (generation != _generation || _sessionChanging || !_sessionReady) { return SessionChanged<Microsoft365Snapshot>(); }
                Expire();
                CacheEntry? cached = _cache.GetValueOrDefault(query);
                if (cached is not null && (cached.Revision > requestedRevision || !refresh && cached.Snapshot is not null))
                {
                    return CachedResult(query);
                }
                linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _sessionLifetime.Token);
                _activeQuery = query;
                _activeAttemptAtUtc = clock.UtcNow;
            }
            using (linked)
            {
                Result<Microsoft365Data> result = await reader.ReadAsync(query, linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    if (generation != _generation) { return SessionChanged<Microsoft365Snapshot>(); }
                    Expire();
                    Microsoft365Snapshot? snapshot = _cache.GetValueOrDefault(query)?.Snapshot;
                    if (result.IsSuccess)
                    {
                        snapshot = new(query, result.Value, clock.UtcNow, false, null,
                            result.Value.Licenses.Select(license => Capacity(license, options.Value.LicenseWarningRatio)).ToArray());
                    }
                    else if (snapshot is not null)
                    {
                        snapshot = snapshot with { Stale = true, RefreshError = result.Error };
                    }
                    if (_cache.Count >= options.Value.MaximumEntries && !_cache.ContainsKey(query))
                    {
                        _cache.Remove(_cache.MinBy(pair => pair.Value.LastAttemptAtUtc).Key);
                    }
                    _cache[query] = new(snapshot, _activeAttemptAtUtc ?? clock.UtcNow, result.Error, ++_revision);
                    _activeQuery = null;
                    _activeAttemptAtUtc = null;
                    return CachedResult(query);
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                if (generation == _generation) { _activeQuery = null; _activeAttemptAtUtc = null; }
            }
            _readGate.Release();
        }
    }

    internal Task<Microsoft365Correlation> ContextAsync(string? sid, string? upn, string? host,
        string? entraDeviceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Expire();
            Microsoft365Snapshot? users = _cache.GetValueOrDefault(new(Microsoft365Resource.Users))?.Snapshot;
            Microsoft365Snapshot? devices = _cache.GetValueOrDefault(new(Microsoft365Resource.Devices))?.Snapshot;
            Microsoft365Snapshot? managed = _cache.GetValueOrDefault(new(Microsoft365Resource.ManagedDevices))?.Snapshot;
            Microsoft365Snapshot[] deviceReads = _cache.Values.Select(entry => entry.Snapshot)
                .Where(snapshot => snapshot?.Query.Resource is Microsoft365Resource.Device or Microsoft365Resource.Devices && snapshot.Data is not null)
                .Select(snapshot => snapshot!).ToArray();
            List<Microsoft365Device> deviceEvidence = [];
            foreach (Microsoft365Snapshot read in deviceReads)
            {
                deviceEvidence.AddRange(read.Data!.Devices.Where(device => !deviceEvidence.Contains(device)).ToArray());
            }
            bool user = host is null && entraDeviceId is null;
            Microsoft365Snapshot? source = user ? users : devices;
            Microsoft365Correlation context = user
                ? Microsoft365CorrelationPolicy.User(users?.Data?.Users ?? [], sid, upn, users?.Data?.Truncated == false)
                : Microsoft365CorrelationPolicy.Device(deviceEvidence, managed?.Data?.ManagedDevices ?? [], host, entraDeviceId, devices?.Data?.Truncated == false);
            return Task.FromResult(context with
            {
                ObservedAtUtc = user ? source?.UpdatedAtUtc : deviceReads.Length == 0 ? null : deviceReads.Min(read => read.UpdatedAtUtc),
                Stale = user ? source is null || IsStale(source)
                    : deviceReads.Length == 0 || deviceReads.Any(IsStale) || managed is null || IsStale(managed),
                Explanation = context.Explanation + (source?.Data?.Truncated == true ? " Source inventory is incomplete." : string.Empty),
            });
        }
    }

    public Task<Result<Microsoft365DeviceContext>> ReadCachedAsync(string? tenantId, string? deviceObjectId,
        CancellationToken cancellationToken, string? managedDeviceId = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tenantId is not null && !ValidId(tenantId) || deviceObjectId is not null && !ValidId(deviceObjectId)
            || managedDeviceId is not null && !ValidId(managedDeviceId))
        {
            return Task.FromResult(Result.Failure<Microsoft365DeviceContext>(new(ErrorCode.InvalidRequest, "Use valid tenant and device object IDs.")));
        }
        lock (_gate)
        {
            Expire();
            string? scope = ValidId(Connection.Configuration.TenantId) ? Guid.Parse(Connection.Configuration.TenantId).ToString("D") : null;
            if (tenantId is not null && !string.Equals(Guid.Parse(tenantId).ToString("D"), scope, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(SessionChanged<Microsoft365DeviceContext>());
            }
            List<Microsoft365Query> deviceQueries = [new(Microsoft365Resource.Devices)];
            if (deviceObjectId is not null)
            {
                deviceQueries.Add(new(Microsoft365Resource.Device, Guid.Parse(deviceObjectId).ToString("D")));
            }
            else
            {
                deviceQueries.AddRange(_cache.Keys.Where(query => query.Resource == Microsoft365Resource.Device));
            }
            Microsoft365Query managed = new(Microsoft365Resource.ManagedDevices);
            Microsoft365Query[] managedQueries = managedDeviceId is null
                ? _cache.Keys.Where(query => query.Resource == Microsoft365Resource.ManagedDevice).ToArray()
                : [new(Microsoft365Resource.ManagedDevice, Guid.Parse(managedDeviceId).ToString("D"))];
            Microsoft365Query? owners = deviceObjectId is null ? null : new(Microsoft365Resource.DeviceOwners, Guid.Parse(deviceObjectId).ToString("D"));
            return Task.FromResult(Result.Success(new Microsoft365DeviceContext(scope, _generation, _revision,
                deviceQueries.Select(query => new CachedEntraDevices(State(query), DeviceData(query))).ToArray(),
                new(State(managed), Connection.Connected && Connection.Configuration.EnableIntune ? _cache.GetValueOrDefault(managed)?.Snapshot?.Data?.ManagedDevices ?? [] : []),
                owners is null ? null : new(State(owners), Connection.Connected ? _cache.GetValueOrDefault(owners)?.Snapshot?.Data?.Members ?? [] : []))
            {
                ManagedDetails = managedQueries.Select(query => new CachedIntuneDevices(State(query),
                    Connection.Connected && Connection.Configuration.EnableIntune ? _cache.GetValueOrDefault(query)?.Snapshot?.Data?.ManagedDevices ?? [] : [])).ToArray(),
            }));
        }
    }

    private IReadOnlyList<Microsoft365Device> DeviceData(Microsoft365Query query) =>
        Connection.Connected ? _cache.GetValueOrDefault(query)?.Snapshot?.Data?.Devices ?? [] : [];

    public Task<Result<Microsoft365UserContext>> ReadCachedAsync(string? tenantId, string? userObjectId,
        string? securityIdentifier, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? sid = Microsoft365QueryValidation.AccountSid(securityIdentifier);
        if (tenantId is not null && !ValidId(tenantId) || userObjectId is not null && !ValidId(userObjectId)
            || securityIdentifier is not null && sid is null)
        {
            return Task.FromResult(Result.Failure<Microsoft365UserContext>(new(ErrorCode.InvalidRequest, "Use valid tenant/user GUIDs and an exact AD account SID.")));
        }
        lock (_gate)
        {
            Expire();
            string? scope = ValidId(Connection.Configuration.TenantId) ? Guid.Parse(Connection.Configuration.TenantId).ToString("D") : null;
            if (tenantId is not null && !string.Equals(Guid.Parse(tenantId).ToString("D"), scope, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(SessionChanged<Microsoft365UserContext>());
            }
            string? userId = userObjectId is null ? null : Guid.Parse(userObjectId).ToString("D");
            List<Microsoft365Query> users = [new(Microsoft365Resource.Users)];
            if (userId is not null) { users.Add(new(Microsoft365Resource.User, userId)); }
            users.AddRange(_cache.Keys.Where(query => query.Resource == Microsoft365Resource.User && query.ObjectId != userId));
            if (sid is not null) { users.Add(new(Microsoft365Resource.UsersBySid, SecurityIdentifier: sid)); }
            Microsoft365Query tenantLicenses = new(Microsoft365Resource.Licenses);
            Microsoft365Query licenses = new(Microsoft365Resource.UserLicenses, userId);
            Microsoft365Query groups = new(Microsoft365Resource.UserGroups, userId);
            Microsoft365Query devices = new(Microsoft365Resource.UserDevices, userId);
            Microsoft365Query activity = new(Microsoft365Resource.UserActivity, userId);
            Microsoft365Query registration = new(Microsoft365Resource.UserRegistration, userId);
            Microsoft365Query[] managed = new[] { new Microsoft365Query(Microsoft365Resource.ManagedDevices) }
                .Concat(_cache.Keys.Where(query => query.Resource == Microsoft365Resource.ManagedDevice)).ToArray();
            HashSet<Guid> associatedIds = managed.SelectMany(query => CachedData(query)?.ManagedDevices ?? [])
                .Where(device => userId is not null && ValidId(device.Id) && ValidId(device.UserId) && Guid.Parse(device.UserId!) == Guid.Parse(userId))
                .Select(device => Guid.Parse(device.Id!)).ToHashSet();
            return Task.FromResult(Result.Success(new Microsoft365UserContext(scope, _generation, _revision,
                users.Select(query => new CachedEntraUsers(State(query), CachedData(query)?.Users ?? [])).ToArray(),
                new(State(tenantLicenses), CachedData(tenantLicenses)?.Licenses ?? []),
                userId is null ? null : new(State(licenses), CachedData(licenses)?.Licenses ?? []),
                userId is null ? null : new(State(groups), CachedData(groups)?.Groups ?? []),
                userId is null ? null : new(State(devices), CachedData(devices)?.Devices ?? []),
                managed.Select(query => new CachedIntuneDevices(State(query),
                    Connection.Configuration.EnableIntune ? (CachedData(query)?.ManagedDevices ?? [])
                        .Where(device => userId is not null && (ValidId(device.UserId) && Guid.Parse(device.UserId!) == Guid.Parse(userId)
                            || ValidId(device.Id) && associatedIds.Contains(Guid.Parse(device.Id!)))).ToArray() : [])).ToArray(),
                userId is null ? null : new(State(activity), CachedData(activity)?.Activity),
                userId is null ? null : new(State(registration), CachedData(registration)?.Activity))));
        }
    }

    private Microsoft365Data? CachedData(Microsoft365Query query) => Connection.Connected
        ? _cache.GetValueOrDefault(query)?.Snapshot?.Data : null;

    public Task<Result<Microsoft365GroupContext>> ReadGroupCachedAsync(string? tenantId, string? groupObjectId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tenantId is not null && !ValidId(tenantId) || groupObjectId is not null && !ValidId(groupObjectId))
        {
            return Task.FromResult(Result.Failure<Microsoft365GroupContext>(new(ErrorCode.InvalidRequest, "Use valid tenant and group GUIDs.")));
        }
        lock (_gate)
        {
            Expire();
            string? scope = ValidId(Connection.Configuration.TenantId) ? Guid.Parse(Connection.Configuration.TenantId).ToString("D") : null;
            if (tenantId is not null && Guid.Parse(tenantId).ToString("D") != scope)
            {
                return Task.FromResult(SessionChanged<Microsoft365GroupContext>());
            }
            string? groupId = groupObjectId is null ? null : Guid.Parse(groupObjectId).ToString("D");
            List<Microsoft365Query> groups = [new(Microsoft365Resource.Groups)];
            if (groupId is not null) { groups.Add(new(Microsoft365Resource.Group, groupId)); }
            else { groups.AddRange(_cache.Keys.Where(query => query.Resource == Microsoft365Resource.Group)); }
            Microsoft365Query members = new(Microsoft365Resource.GroupMembers, groupId);
            return Task.FromResult(Result.Success(new Microsoft365GroupContext(scope, _generation, _revision,
                groups.Select(query => new CachedMicrosoft365Groups(State(query), CachedData(query)?.Groups ?? [])).ToArray(),
                groupId is null ? null : new(State(members), CachedData(members)?.Members ?? []))));
        }
    }

    public Task<Result<Microsoft365ObjectLists>> ReadObjectListsCachedAsync(string? tenantId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tenantId is not null && !ValidId(tenantId))
        {
            return Task.FromResult(Result.Failure<Microsoft365ObjectLists>(new(ErrorCode.InvalidRequest, "Use a valid tenant GUID.")));
        }
        lock (_gate)
        {
            Expire();
            string? scope = ValidId(Connection.Configuration.TenantId) ? Guid.Parse(Connection.Configuration.TenantId).ToString("D") : null;
            if (tenantId is not null && Guid.Parse(tenantId).ToString("D") != scope)
            {
                return Task.FromResult(SessionChanged<Microsoft365ObjectLists>());
            }
            Microsoft365Query[] inventories = [new(Microsoft365Resource.Users), new(Microsoft365Resource.Groups),
                new(Microsoft365Resource.Devices), new(Microsoft365Resource.ManagedDevices)];
            IEnumerable<Microsoft365Query> queries = inventories.Concat(_cache.Keys.Where(query => IsObjectListResource(query.Resource))
                .OrderBy(query => query.Resource).ThenBy(query => query.ObjectId, StringComparer.Ordinal)
                .ThenBy(query => query.SecurityIdentifier, StringComparer.Ordinal)).Distinct();
            var reads = new List<Microsoft365ObjectListRead>();
            int cached = 0;
            int loaded = 0;
            foreach (Microsoft365Query query in queries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Microsoft365ReadState state = State(query);
                Microsoft365Data? data = state.Availability == Microsoft365Availability.Available ? CachedData(query) : null;
                var rows = new List<Microsoft365ObjectListRow>();
                if (data is not null)
                {
                    foreach (Microsoft365ObjectListRow row in ObjectListRows(query, data))
                    {
                        ++cached;
                        if (loaded >= options.Value.MaximumObjectListRecords) { continue; }
                        rows.Add(row); ++loaded;
                    }
                }
                reads.Add(new(state, rows));
            }
            return Task.FromResult(Result.Success(new Microsoft365ObjectLists(scope, _generation, _revision,
                options.Value.MaximumObjectListRecords, cached, loaded, cached > loaded, reads)));
        }
    }

    private static bool IsObjectListResource(Microsoft365Resource resource) => resource is
        Microsoft365Resource.Users or Microsoft365Resource.User or Microsoft365Resource.UsersBySid
        or Microsoft365Resource.Groups or Microsoft365Resource.Group or Microsoft365Resource.UserGroups
        or Microsoft365Resource.Devices or Microsoft365Resource.Device or Microsoft365Resource.UserDevices
        or Microsoft365Resource.ManagedDevices or Microsoft365Resource.ManagedDevice;

    private static IEnumerable<Microsoft365ObjectListRow> ObjectListRows(Microsoft365Query query, Microsoft365Data data)
    {
        switch (query.Resource)
        {
            case Microsoft365Resource.Users or Microsoft365Resource.User or Microsoft365Resource.UsersBySid:
                foreach (Microsoft365User user in data.Users)
                {
                    yield return new(ObjectKind.User, ObjectSource.Entra, user.Id, user.DisplayName, user.UserPrincipalName,
                        user.AccountEnabled, null, user.OnPremisesSid, null, null,
                        user.AssignedLicenses?.Where(license => !string.IsNullOrWhiteSpace(license.SkuId)).Select(license => license.SkuId!).ToArray());
                }
                break;
            case Microsoft365Resource.Groups or Microsoft365Resource.Group or Microsoft365Resource.UserGroups:
                foreach (Microsoft365Group group in data.Groups)
                {
                    yield return new(ObjectKind.Group, ObjectSource.Entra, group.Id, group.DisplayName, null, null, null, null, null, null, null);
                }
                break;
            case Microsoft365Resource.Devices or Microsoft365Resource.Device or Microsoft365Resource.UserDevices:
                foreach (Microsoft365Device device in data.Devices)
                {
                    yield return new(ObjectKind.Device, ObjectSource.Entra, device.Id, device.DisplayName, null, device.AccountEnabled,
                        device.OperatingSystem, null, device.DeviceId, null, null);
                }
                break;
            case Microsoft365Resource.ManagedDevices or Microsoft365Resource.ManagedDevice:
                foreach (Microsoft365ManagedDevice device in data.ManagedDevices)
                {
                    yield return new(ObjectKind.Device, ObjectSource.Intune, device.Id, device.DeviceName, device.UserPrincipalName, null,
                        device.OperatingSystem, null, device.EntraDeviceId, device.UserId, null);
                }
                break;
        }
    }

    private static bool ValidId(string? id) => Guid.TryParse(id, out Guid value) && value != Guid.Empty;

    internal static Microsoft365LicenseCapacity Capacity(Microsoft365License license, double warningRatio)
    {
        bool valid = license.AppliesTo == "User" && license.EnabledSeats is >= 0 && license.ConsumedSeats is >= 0;
        int? remaining = valid ? license.EnabledSeats - license.ConsumedSeats : null;
        return new(license.SkuId, remaining, valid ? license.EnabledSeats > 0
            && (double)license.ConsumedSeats!.Value / license.EnabledSeats.Value >= warningRatio : null,
            valid ? remaining < 0 : null);
    }

    private Microsoft365Connection Connection => !_sessionReady
        ? reader.Connection with { Connected = false, Account = null } : reader.Connection;

    private Result<Microsoft365Snapshot> CachedResult(Microsoft365Query query)
    {
        CacheEntry entry = _cache[query];
        return entry.Snapshot is null ? Result.Failure<Microsoft365Snapshot>(entry.Error!)
            : Result.Success(entry.Snapshot with { Stale = IsStale(entry.Snapshot), State = State(query) });
    }

    private Microsoft365ReadState State(Microsoft365Query query)
    {
        CacheEntry? entry = _cache.GetValueOrDefault(query);
        Microsoft365Snapshot? snapshot = entry?.Snapshot;
        Microsoft365Data? data = snapshot?.Data;
        Microsoft365Connection connection = Connection;
        bool disabled = query.Resource is Microsoft365Resource.ManagedDevices or Microsoft365Resource.ManagedDevice && !connection.Configuration.EnableIntune
            || query.Resource is Microsoft365Resource.UserActivity or Microsoft365Resource.UserRegistration && !connection.Configuration.EnableAuthenticationReports;
        Microsoft365Availability availability = !connection.Connected ? Microsoft365Availability.NotConnected
            : disabled ? Microsoft365Availability.NotEnabled
            : snapshot is not null ? Microsoft365Availability.Available
            : entry?.Error is not null ? Microsoft365Availability.Unavailable : Microsoft365Availability.NotCached;
        EvidenceFreshness freshness = snapshot?.UpdatedAtUtc is null || snapshot.UpdatedAtUtc > clock.UtcNow
            ? EvidenceFreshness.Unknown : IsStale(snapshot) ? EvidenceFreshness.Stale : EvidenceFreshness.Fresh;
        return new(query, _sessionChanging ? null : connection.Configuration.TenantId, _generation, entry?.Revision ?? _revision,
            availability, _activeQuery == query, snapshot?.UpdatedAtUtc,
            _activeQuery == query ? _activeAttemptAtUtc : entry?.LastAttemptAtUtc, entry?.Error,
            snapshot?.UpdatedAtUtc + options.Value.RetainFor, freshness,
            data is null ? EvidenceCoverage.Unknown : data.Truncated ? EvidenceCoverage.Partial : EvidenceCoverage.ReturnedSet,
            data is null ? null : data.Tenants.Count + data.Users.Count + data.Groups.Count + data.Devices.Count
                + data.ManagedDevices.Count + data.Licenses.Count + data.Members.Count + (data.Activity is null ? 0 : 1), data?.TotalCount,
            snapshot?.UpdatedAtUtc + options.Value.FreshFor);
    }

    private bool IsStale(Microsoft365Snapshot snapshot) => snapshot.RefreshError is not null || snapshot.UpdatedAtUtc is null
        || snapshot.UpdatedAtUtc > clock.UtcNow || clock.UtcNow - snapshot.UpdatedAtUtc >= options.Value.FreshFor;

    private void Expire()
    {
        foreach ((Microsoft365Query query, CacheEntry entry) in _cache.ToArray())
        {
            DateTimeOffset retainedFrom = entry.Snapshot?.UpdatedAtUtc ?? entry.LastAttemptAtUtc;
            if (clock.UtcNow - retainedFrom < options.Value.RetainFor) { continue; }
            if (entry.Error is not null && clock.UtcNow - entry.LastAttemptAtUtc < options.Value.RetainFor)
            {
                _cache[query] = entry with { Snapshot = null, Revision = ++_revision };
            }
            else { _cache.Remove(query); ++_revision; }
        }
    }

    private static Result<T> SessionChanged<T>() => Result.Failure<T>(new Error(ErrorCode.Microsoft365NotConnected,
        "Microsoft 365 session changed. Reopen the view."));

    public void Dispose() { _readGate.Dispose(); _sessionLifetime.Dispose(); }
}
