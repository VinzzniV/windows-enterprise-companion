namespace Wec.Modules.EmployeeLifecycle;

public sealed class EmployeeLifecycleOptions
{
    public const string SectionName = "Wec:EmployeeLifecycle";

    /// <summary>Due-date offset for the "delete AD account after retention" offboarding task.</summary>
    public int AdAccountDeletionRetentionDays { get; set; } = 30;

    public int AuditHistoryLimit { get; set; } = 200;
}
