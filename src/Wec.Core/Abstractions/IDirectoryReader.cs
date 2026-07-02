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
}
