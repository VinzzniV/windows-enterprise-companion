namespace Wec.Modules.PatchManagement;

public sealed class PatchManagementOptions
{
    public const string SectionName = "Wec:PatchManagement";

    /// <summary>
    /// Depot the UI preselects (matched against depot id and description,
    /// case-insensitive substring). Depots double as locations (ADR 0008).
    /// </summary>
    public string DefaultDepotFilter { get; set; } = "Denkingen";

    public TimeSpan OpsiRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>opsiconfd default port, used when the server input names none.</summary>
    public int DefaultServicePort { get; set; } = 4447;

    public int AuditHistoryLimit { get; set; } = 100;
}
