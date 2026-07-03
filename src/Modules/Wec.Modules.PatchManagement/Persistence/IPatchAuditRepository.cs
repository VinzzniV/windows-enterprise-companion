namespace Wec.Modules.PatchManagement.Persistence;

public sealed record PatchAuditEntry(
    long Id,
    DateTimeOffset TimestampUtc,
    string UserName,
    string Action,
    string? ProductId,
    string? DepotId,
    IReadOnlyList<string> TargetClients,
    string? PreviewJson,
    string Result,
    string? ErrorMessage);

public interface IPatchAuditRepository
{
    Task AddAsync(PatchAuditEntry entry, CancellationToken cancellationToken);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<PatchAuditEntry>> ListAsync(int limit, CancellationToken cancellationToken);
}
