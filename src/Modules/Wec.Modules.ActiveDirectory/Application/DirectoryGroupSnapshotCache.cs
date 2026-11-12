using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;

namespace Wec.Modules.ActiveDirectory.Application;

internal sealed record DirectoryGroupReadData(DirectoryGroupIdentityResult? Identity = null,
    DirectoryGroupMemberPage? Members = null, DirectoryGroupPage? Page = null)
{
    internal DateTimeOffset? RetrievedAtUtc => Identity?.RetrievedAtUtc ?? Members?.RetrievedAtUtc ?? Page?.RetrievedAtUtc;
}
internal sealed record DirectoryGroupStoredRead(DirectoryGroupReadState State, DirectoryGroupReadData Data);

internal sealed class DirectoryGroupSnapshotCache(IClock clock, IOptions<ActiveDirectoryOptions> options) : IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private readonly Dictionary<string, DirectoryGroupStoredRead> _reads = new(StringComparer.OrdinalIgnoreCase);
    private string? _context;
    private long _session;
    private long _revision;
    internal static string IdentityKey(DirectoryGroupIdentityQuery query) => $"identity:{query.ObjectId?.ToString("D") ?? query.SecurityIdentifier ?? query.DistinguishedName}";
    internal static string MembersKey(DirectoryGroupMemberQuery query) => $"members:{query.GroupObjectId:D}:{query.Page}:{query.PageSize}";
    internal static string PageKey(DirectoryGroupPageQuery query) => $"page:{query.Page}:{query.PageSize}:{query.Search?.Trim()}";

    internal DirectoryGroupStoredRead Read(DirectoryUserReadConnection connection, string scope, string key,
        CancellationToken cancellationToken, Guid? groupId = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Activate(connection, scope);
            Expire();
            DirectoryGroupStoredRead? read = _reads.GetValueOrDefault(key);
            if (read is null && groupId is { } id)
            {
                read = _reads.Values.Where(value => value.Data.Identity is { Truncated: false, Groups.Count: 1 } identity
                    && identity.Groups[0].ObjectId == id).MaxBy(value => value.State.Revision);
            }
            return read is null ? new(new(null, null, null, _session, _revision, null, null, true), new())
                : read with { State = read.State with { Stale = read.State.LastAttemptError is not null || read.State.RetrievedAtUtc is null
                    || read.State.RetrievedAtUtc > clock.UtcNow || read.State.FreshUntilUtc <= clock.UtcNow } };
        }
    }

    internal async Task<Result<DirectoryGroupReadData>> LoadAsync(DirectoryUserReadConnection connection, string scope, string key,
        Func<CancellationToken, Task<Result<DirectoryGroupReadData>>> load, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long session;
        long revision;
        lock (_gate) { Activate(connection, scope); session = _session; revision = _revision; }
        await _readGate.WaitAsync(cancellationToken);
        try
        {
            lock (_gate)
            {
                if (session != _session) { return Changed(); }
                Expire();
                if (_reads.GetValueOrDefault(key) is { } cached && cached.State.Revision > revision)
                {
                    return cached.State.LastAttemptError is { } error ? Result.Failure<DirectoryGroupReadData>(error) : Result.Success(cached.Data);
                }
            }
            DateTimeOffset attempt = clock.UtcNow;
            Result<DirectoryGroupReadData> result = await load(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (session != _session) { return Changed(); }
                Expire();
                DirectoryGroupReadData data = result.IsSuccess ? result.Value : _reads.GetValueOrDefault(key)?.Data ?? new();
                if (!_reads.ContainsKey(key) && _reads.Count >= options.Value.IdentityCacheMaximumEntries)
                {
                    _reads.Remove(_reads.MinBy(pair => pair.Value.State.LastAttemptAtUtc).Key);
                }
                _reads[key] = new(new(data.RetrievedAtUtc, attempt, result.Error, _session, ++_revision,
                    data.RetrievedAtUtc + options.Value.IdentityCacheRetainFor, data.RetrievedAtUtc + options.Value.IdentityCacheFreshFor, result.IsFailure), data);
                return result;
            }
        }
        finally { _readGate.Release(); }
    }

    private void Activate(DirectoryUserReadConnection connection, string scope)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { connection, scope });
        string context;
        try { context = Convert.ToHexString(SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        if (context == _context) { return; }
        _context = context; _reads.Clear(); ++_session; ++_revision;
    }
    private void Expire()
    {
        foreach ((string key, DirectoryGroupStoredRead read) in _reads.ToArray())
        {
            if (clock.UtcNow - (read.State.RetrievedAtUtc ?? read.State.LastAttemptAtUtc) < options.Value.IdentityCacheRetainFor) { continue; }
            if (read.State.LastAttemptError is not null && clock.UtcNow - read.State.LastAttemptAtUtc < options.Value.IdentityCacheRetainFor)
            {
                _reads[key] = new(read.State with { RetrievedAtUtc = null, RetainedUntilUtc = null, FreshUntilUtc = null, Revision = ++_revision, Stale = true }, new());
            }
            else { _reads.Remove(key); ++_revision; }
        }
    }
    private static Result<DirectoryGroupReadData> Changed() => Result.Failure<DirectoryGroupReadData>(new(ErrorCode.DirectoryUnavailable,
        "The directory connection changed. Reopen the group in its selected scope."));
    public void Dispose() => _readGate.Dispose();
}
