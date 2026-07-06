namespace Wec.Modules.Targets.Persistence;

/// <summary>Known roles a saved target can carry (wire values, ADR 0003 casing aside — these are UI labels).</summary>
public static class TargetRoles
{
    public const string Client = "Client";
    public const string PrintServer = "PrintServer";
    public const string OpsiServer = "OpsiServer";
    public const string DomainController = "DomainController";
    public const string Generic = "Generic";

    public static readonly IReadOnlyList<string> All =
        [Client, PrintServer, OpsiServer, DomainController, Generic];

    public static bool IsKnown(string role) => All.Contains(role);
}

public sealed record SavedTarget(
    int Id, string Label, string Host, string Role, string? UserName, DateTimeOffset CreatedAtUtc);

public interface ISavedTargetRepository
{
    Task<IReadOnlyList<SavedTarget>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Insert or update the saved target keyed on (host, role), case-insensitively.</summary>
    Task<SavedTarget> UpsertAsync(
        string label, string host, string role, string? userName,
        DateTimeOffset createdAtUtc, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
