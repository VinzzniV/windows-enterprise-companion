using Wec.Core.Results;

namespace Wec.Core.Abstractions;

/// <summary>
/// Read-only directory access seam (ADR 0006). The interface deliberately
/// exposes search only — modules cannot write to the directory through it.
/// Authentication is always the current Windows identity.
/// </summary>
public interface IDirectoryReader
{
    Task<Result<IReadOnlyList<DirectoryEntryData>>> SearchAsync(
        DirectorySearchQuery query,
        CancellationToken cancellationToken);

    /// <summary>
    /// Counts every LDAP match while retaining at most <paramref name="entryLimit"/>
    /// entries. This keeps overview and example queries exact without
    /// materializing an entire directory on the client.
    /// </summary>
    Task<Result<BoundedDirectorySearchResult>> SearchBoundedAsync(
        DirectorySearchQuery query,
        int entryLimit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Counts every LDAP match while retaining only one offset-based result
    /// window. Callers must provide a stable server-side sort on the query.
    /// </summary>
    Task<Result<BoundedDirectorySearchResult>> SearchPageAsync(
        DirectorySearchQuery query,
        int entryOffset,
        int entryLimit,
        CancellationToken cancellationToken);
}

public sealed record BoundedDirectorySearchResult(
    int TotalCount,
    IReadOnlyList<DirectoryEntryData> Entries);
