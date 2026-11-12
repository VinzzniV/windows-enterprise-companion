using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;

namespace Wec.Modules.ActiveDirectory.Application;

internal sealed class DirectoryUserListCache(IClock clock, IOptions<ActiveDirectoryOptions> options) : IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private readonly Dictionary<(string? Search, int Page, int Size), CachedDirectoryUserList> _pages = [];
    private string? _context;
    private long _session;
    private long _revision;

    internal CachedDirectoryUserList Read(DirectoryUserListQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Activate(query);
            Expire();
            return Current(query);
        }
    }

    internal async Task<Result<CachedDirectoryUserList>> LoadAsync(DirectoryUserListQuery query,
        Func<CancellationToken, Task<Result<DirectoryUserListPage>>> load, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long session;
        long revision;
        lock (_gate) { Activate(query); session = _session; revision = _revision; }
        await _readGate.WaitAsync(cancellationToken);
        try
        {
            lock (_gate)
            {
                if (session != _session) { return Changed(); }
                Expire();
                CachedDirectoryUserList current = Current(query);
                if (current.LastAttemptAtUtc is not null && current.Revision > revision) { return Result.Success(current); }
            }
            DateTimeOffset attempt = clock.UtcNow;
            Result<DirectoryUserListPage> result = await load(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (session != _session) { return Changed(); }
                Expire();
                var key = (query.Search, query.Page, query.PageSize);
                DirectoryUserListPage? data = result.IsSuccess ? result.Value : _pages.GetValueOrDefault(key)?.Data;
                if (!_pages.ContainsKey(key) && _pages.Count >= options.Value.IdentityCacheMaximumEntries)
                {
                    _pages.Remove(_pages.MinBy(pair => pair.Value.LastAttemptAtUtc).Key);
                }
                _pages[key] = new(data, attempt, result.Error, _session, ++_revision,
                    data?.RetrievedAtUtc + options.Value.IdentityCacheRetainFor,
                    data?.RetrievedAtUtc + options.Value.IdentityCacheFreshFor, result.IsFailure);
                return Result.Success(Current(query));
            }
        }
        finally { _readGate.Release(); }
    }

    private CachedDirectoryUserList Current(DirectoryUserListQuery query)
    {
        CachedDirectoryUserList? current = _pages.GetValueOrDefault((query.Search, query.Page, query.PageSize));
        return current is null ? new(null, null, null, _session, _revision, null, null, true)
            : current with { Stale = current.LastAttemptError is not null || current.Data is null
                || current.Data.RetrievedAtUtc > clock.UtcNow || current.FreshUntilUtc <= clock.UtcNow };
    }

    private void Activate(DirectoryUserListQuery query)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { query.Connection, query.DirectoryScope });
        string fingerprint;
        try { fingerprint = Convert.ToHexString(SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        if (fingerprint == _context) { return; }
        _context = fingerprint; _pages.Clear(); ++_session; ++_revision;
    }

    private void Expire()
    {
        foreach ((var key, CachedDirectoryUserList value) in _pages.ToArray())
        {
            if (clock.UtcNow - (value.Data?.RetrievedAtUtc ?? value.LastAttemptAtUtc) < options.Value.IdentityCacheRetainFor) { continue; }
            if (value.LastAttemptError is not null && clock.UtcNow - value.LastAttemptAtUtc < options.Value.IdentityCacheRetainFor)
            {
                _pages[key] = value with { Data = null, RetainedUntilUtc = null, FreshUntilUtc = null, Revision = ++_revision, Stale = true };
            }
            else { _pages.Remove(key); ++_revision; }
        }
    }

    private static Result<CachedDirectoryUserList> Changed() => Result.Failure<CachedDirectoryUserList>(new(ErrorCode.DirectoryUnavailable,
        "The directory context changed. Read the user list again in the selected scope."));
    public void Dispose() => _readGate.Dispose();
}
