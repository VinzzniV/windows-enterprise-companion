namespace Wec.Modules.EmployeeLifecycle.Domain;

public enum EmployeeStatus
{
    Planned = 0,
    Onboarding,
    Active,
    Changing,
    Offboarding,
    Disabled,
}

public enum CaseType
{
    Onboarding = 0,
    Offboarding,
    Change,
}

public enum CaseStatus
{
    Active = 0,
    Completed,
    Cancelled,
}

/// <summary>Named LifecycleTaskStatus to avoid clashing with System.Threading.Tasks.TaskStatus.</summary>
public enum LifecycleTaskStatus
{
    Open = 0,
    InProgress,
    Blocked,
    Done,
    Skipped,
}

public enum TaskArea
{
    General = 0,
    Account,
    Hardware,
    Software,
    Permissions,
    Mailbox,
}
