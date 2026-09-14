using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;

namespace Wec.Modules.ActiveDirectory.Application;

internal sealed class DirectoryUserSnapshotCache(IClock clock, IOptions<ActiveDirectoryOptions> options) : IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private readonly Dictionary<string, CachedDirectoryUser> _cache = new(StringComparer.OrdinalIgnoreCase);
    private string? _context;
    private long _session;
    private long _revision;

    internal CachedDirectoryUser? Read(DirectoryUserLookup query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Activate(query);
            return Read(Key(query));
        }
    }

    internal async Task<Result<DirectoryUserIdentityResult>> LoadAsync(DirectoryUserLookup query,
        Func<CancellationToken, Task<Result<DirectoryUserIdentityResult>>> load, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string key = Key(query);
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
                CachedDirectoryUser? cached = Read(key);
                if (cached?.Revision > requestedRevision)
                {
                    return cached.LastAttemptError is not null ? Result.Failure<DirectoryUserIdentityResult>(cached.LastAttemptError)
                        : Result.Success(cached.Data!);
                }
            }
            DateTimeOffset attempt = clock.UtcNow;
            Result<DirectoryUserIdentityResult> result = await load(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (session != _session) { return Changed(); }
                CachedDirectoryUser? previous = Read(key);
                DirectoryUserIdentityResult? data = result.IsSuccess ? result.Value : previous?.Data;
                if (_cache.Count >= options.Value.IdentityCacheMaximumEntries && !_cache.ContainsKey(key))
                {
                    _cache.Remove(_cache.MinBy(pair => pair.Value.LastAttemptAtUtc).Key);
                }
                _cache[key] = new(data, attempt, result.Error, _session, ++_revision,
                    data?.RetrievedAtUtc + options.Value.IdentityCacheRetainFor, result.IsFailure, data?.RetrievedAtUtc + options.Value.IdentityCacheFreshFor);
                return result;
            }
        }
        finally { _readGate.Release(); }
    }

    private CachedDirectoryUser? Read(string key)
    {
        if (!_cache.TryGetValue(key, out CachedDirectoryUser? cached)) { return null; }
        if (clock.UtcNow - (cached.Data?.RetrievedAtUtc ?? cached.LastAttemptAtUtc) >= options.Value.IdentityCacheRetainFor)
        {
            if (cached.LastAttemptError is not null && clock.UtcNow - cached.LastAttemptAtUtc < options.Value.IdentityCacheRetainFor)
            {
                cached = cached with { Data = null, RetainedUntilUtc = null, FreshUntilUtc = null, Revision = ++_revision };
                _cache[key] = cached;
            }
            else { _cache.Remove(key); ++_revision; return null; }
        }
        return cached with { Stale = cached.LastAttemptError is not null || cached.Data is null
            || cached.Data.RetrievedAtUtc > clock.UtcNow || clock.UtcNow - cached.Data.RetrievedAtUtc >= options.Value.IdentityCacheFreshFor };
    }

    private void Activate(DirectoryUserLookup query)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new { query.Connection, query.DirectoryScope });
        string context;
        try { context = Convert.ToHexString(SHA256.HashData(json)); }
        finally { CryptographicOperations.ZeroMemory(json); }
        if (string.Equals(context, _context, StringComparison.Ordinal)) { return; }
        _context = context;
        _cache.Clear();
        ++_session;
        ++_revision;
    }

    private static string Key(DirectoryUserLookup query) => query.ObjectId is { } id
        ? $"guid:{id:D}" : $"sid:{query.SecurityIdentifier}";

    private static Result<DirectoryUserIdentityResult> Changed() => Result.Failure<DirectoryUserIdentityResult>(
        new(ErrorCode.DirectoryUnavailable, "Directory context changed. Reopen the profile in the selected context."));

    public void Dispose() => _readGate.Dispose();
}
