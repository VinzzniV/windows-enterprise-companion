namespace Wec.Modules.EmployeeLifecycle.Domain;

/// <summary>
/// The single source of truth for employee/case status transitions.
/// The frontend only renders these rules; it never defines its own.
/// </summary>
public static class LifecycleRules
{
    public static bool CanStartCase(EmployeeStatus employeeStatus, CaseType caseType) => caseType switch
    {
        CaseType.Onboarding => employeeStatus is EmployeeStatus.Planned or EmployeeStatus.Disabled,
        CaseType.Offboarding => employeeStatus is EmployeeStatus.Active,
        CaseType.Change => employeeStatus is EmployeeStatus.Active,
        _ => false,
    };

    public static EmployeeStatus StatusWhileCaseActive(CaseType caseType) => caseType switch
    {
        CaseType.Onboarding => EmployeeStatus.Onboarding,
        CaseType.Offboarding => EmployeeStatus.Offboarding,
        _ => EmployeeStatus.Changing,
    };

    public static EmployeeStatus StatusAfterCompletion(CaseType caseType) => caseType switch
    {
        CaseType.Onboarding => EmployeeStatus.Active,
        CaseType.Offboarding => EmployeeStatus.Disabled,
        _ => EmployeeStatus.Active,
    };

    /// <summary>Cancelling deterministically restores the pre-case status (no history lookup needed).</summary>
    public static EmployeeStatus StatusAfterCancellation(CaseType caseType) => caseType switch
    {
        CaseType.Onboarding => EmployeeStatus.Planned,
        CaseType.Offboarding => EmployeeStatus.Active,
        _ => EmployeeStatus.Active,
    };

    public static bool IsTerminal(LifecycleTaskStatus taskStatus) =>
        taskStatus is LifecycleTaskStatus.Done or LifecycleTaskStatus.Skipped;
}
