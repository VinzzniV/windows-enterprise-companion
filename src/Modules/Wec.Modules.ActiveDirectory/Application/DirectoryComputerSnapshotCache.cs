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
            return Read(Key(query));
        }
    }

    internal async Task<Result<DirectoryComputerIdentityResult>> LoadAsync(DirectoryComputerIdentityQuery query,
        Func<CancellationToken, Task<Result<DirectoryComputerIdentityResult>>> load, CancellationToken cancellationToken)
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

    private static string Key(DirectoryComputerIdentityQuery query) => query.ObjectId is { } id
        ? $"guid:{id:D}" : $"sid:{query.SecurityIdentifier}";

    private static Result<DirectoryComputerIdentityResult> Changed() => Result.Failure<DirectoryComputerIdentityResult>(
        new(ErrorCode.DirectoryUnavailable, "Directory context changed. Reopen the profile in the selected context."));

    public void Dispose() => _readGate.Dispose();
}
