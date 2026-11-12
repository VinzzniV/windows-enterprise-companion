using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;

namespace Wec.Modules.ActiveDirectory.Application;

internal sealed class DirectoryComputerSnapshotCache(IClock clock, IOptions<ActiveDirectoryOptions> options) : IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private readonly Dictionary<string, CachedDirectoryComputer> _cache = new(StringComparer.OrdinalIgnoreCase);
    private string? _context;
    private long _session;
    private long _revision;

    internal CachedDirectoryComputer? Read(DirectoryComputerIdentityQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Activate(query);
            return Read(Key(query)) ?? FromDiscovery(query);
        }
    }

    internal async Task<Result<DirectoryComputerIdentityResult>> LoadAsync(DirectoryComputerIdentityQuery query,
        Func<CancellationToken, Task<Result<DirectoryComputerIdentityResult>>> load, CancellationToken cancellationToken, string? discoveryKey = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string key = discoveryKey ?? Key(query);
        long session;
        long requestedRevision;
        lock (_gate)
        {
            Activate(query);
            session = _session;
            requestedRevision = _revision;
        }
        await _readGate.WaitAsync(cancellationToken);
        try
        {
            lock (_gate)
            {
                if (session != _session) { return Changed(); }
                CachedDirectoryComputer? cached = Read(key);
                if (cached?.Revision > requestedRevision)
                {
                    return cached.LastAttemptError is not null ? Result.Failure<DirectoryComputerIdentityResult>(cached.LastAttemptError)
                        : Result.Success(cached.Data!);
                }
            }
            DateTimeOffset attempt = clock.UtcNow;
            Result<DirectoryComputerIdentityResult> result = await load(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (session != _session) { return Changed(); }
                CachedDirectoryComputer? previous = Read(key);
                DirectoryComputerIdentityResult? data = result.IsSuccess ? result.Value : previous?.Data;
                if (_cache.Count >= options.Value.IdentityCacheMaximumEntries && !_cache.ContainsKey(key))
                {
                    _cache.Remove(_cache.MinBy(pair => pair.Value.LastAttemptAtUtc).Key);
                }
                _cache[key] = new(data, attempt, result.Error, _session, ++_revision,
                    data?.RetrievedAtUtc + options.Value.IdentityCacheRetainFor, result.IsFailure);
                return result;
            }
        }
        finally { _readGate.Release(); }
    }

    internal static string DiscoveryKey(string? search, int limit) => $"discovery:{limit}:{search}";

    internal CachedDirectoryComputer? ReadDiscovery(DirectoryComputerIdentityQuery context, string key,
        CancellationToken cancellationToken, bool activate = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (activate) { Activate(context); }
            else if (_context != Fingerprint(context)) { return null; }
            return Read(key);
        }
    }

    private CachedDirectoryComputer? FromDiscovery(DirectoryComputerIdentityQuery query)
    {
        if (query.ObjectId is null && query.SecurityIdentifier is null) { return null; }
        var matches = _cache.Keys.Where(key => key.StartsWith("discovery:", StringComparison.Ordinal)).ToArray()
            .Select(Read).Where(read => read?.Data is not null).Select(read => (Read: read!, Computers: read!.Data!.Computers
                .Where(computer => query.ObjectId is { } id ? computer.ObjectId == id
                    : string.Equals(computer.SecurityIdentifier, query.SecurityIdentifier, StringComparison.OrdinalIgnoreCase)).ToArray()))
            .Where(entry => entry.Computers.Length > 0).ToArray();
        if (matches.Length == 0) { return null; }
        CachedDirectoryComputer oldest = matches.MinBy(entry => entry.Read.Data!.RetrievedAtUtc).Read;
        bool failed = matches.Any(entry => entry.Read.LastAttemptError is not null);
        return oldest with
        {
            Data = oldest.Data! with { Computers = matches.SelectMany(entry => entry.Computers).Distinct().ToArray(),
                Truncated = matches.Any(entry => entry.Read.Data!.Truncated) },
            LastAttemptAtUtc = matches.Max(entry => entry.Read.LastAttemptAtUtc),
            LastAttemptError = failed ? new(ErrorCode.DirectoryUnavailable, "A cached discovery query failed. These observations retain their original age.") : null,
            Revision = _revision, Stale = matches.Any(entry => entry.Read.Stale),
        };
    }

    private CachedDirectoryComputer? Read(string key)
    {
        if (!_cache.TryGetValue(key, out CachedDirectoryComputer? cached)) { return null; }
        if (clock.UtcNow - (cached.Data?.RetrievedAtUtc ?? cached.LastAttemptAtUtc) >= options.Value.IdentityCacheRetainFor)
        {
            if (cached.LastAttemptError is not null && clock.UtcNow - cached.LastAttemptAtUtc < options.Value.IdentityCacheRetainFor)
            {
                cached = cached with { Data = null, RetainedUntilUtc = null, Revision = ++_revision };
                _cache[key] = cached;
            }
            else { _cache.Remove(key); ++_revision; return null; }
        }
        return cached with { Stale = cached.LastAttemptError is not null || cached.Data is null
            || cached.Data.RetrievedAtUtc > clock.UtcNow || clock.UtcNow - cached.Data.RetrievedAtUtc >= options.Value.IdentityCacheFreshFor };
    }

    private void Activate(DirectoryComputerIdentityQuery query)
    {
        string context = Fingerprint(query);
        if (string.Equals(context, _context, StringComparison.Ordinal)) { return; }
        _context = context;
        _cache.Clear();
        ++_session;
        ++_revision;
    }

    private static string Fingerprint(DirectoryComputerIdentityQuery query)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new { query.Connection, query.DirectoryScope });
        try { return Convert.ToHexString(SHA256.HashData(json)); }
        finally { CryptographicOperations.ZeroMemory(json); }
    }

    private static string Key(DirectoryComputerIdentityQuery query) => query.ObjectId is { } id
        ? $"guid:{id:D}" : $"sid:{query.SecurityIdentifier}";

    private static Result<DirectoryComputerIdentityResult> Changed() => Result.Failure<DirectoryComputerIdentityResult>(
        new(ErrorCode.DirectoryUnavailable, "Directory context changed. Reopen the profile in the selected context."));

    public void Dispose() => _readGate.Dispose();
}
