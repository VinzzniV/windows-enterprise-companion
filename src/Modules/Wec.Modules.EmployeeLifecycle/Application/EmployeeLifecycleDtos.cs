using Wec.Modules.EmployeeLifecycle.Domain;

namespace Wec.Modules.EmployeeLifecycle.Application;

public sealed record EmployeeSummary(
    long Id,
    string FirstName,
    string LastName,
    string? EmployeeNumber,
    string? Department,
    string? Title,
    string? SamAccountName,
    EmployeeStatus Status,
    DateOnly? EntryDate,
    DateOnly? ExitDate,
    long? ActiveCaseId,
    CaseType? ActiveCaseType,
    int OpenTaskCount,
    int OverdueTaskCount);

public sealed record EmployeeDetails(
    long Id,
    string FirstName,
    string LastName,
    string? Email,
    string? EmployeeNumber,
    string? Department,
    string? Title,
    string? Manager,
    string? SamAccountName,
    string? UserPrincipalName,
    string? DistinguishedName,
    EmployeeStatus Status,
    DateOnly? EntryDate,
    DateOnly? ExitDate,
    string? Notes,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

public sealed record TaskDetails(
    long Id,
    long CaseId,
    string Title,
    TaskArea Area,
    LifecycleTaskStatus Status,
    DateOnly? DueDate,
    string? Assignee,
    string Notes,
    int SortOrder,
    bool IsOverdue);

public sealed record CaseDetails(
    long Id,
    long EmployeeId,
    CaseType Type,
    CaseStatus Status,
    DateOnly? EffectiveDate,
    string? Note,
    string? CancelReason,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? ClosedUtc,
    IReadOnlyList<TaskDetails> Tasks);

public sealed record LifecycleAuditEntry(
    long Id,
    DateTimeOffset TimestampUtc,
    string UserName,
    long EmployeeId,
    long? CaseId,
    long? TaskId,
    string EventType,
    string? OldValue,
    string? NewValue,
    string? Detail);

public sealed record EmployeeListResult(IReadOnlyList<EmployeeSummary> Employees);

public sealed record EmployeeDetailsResult(EmployeeDetails Employee, IReadOnlyList<CaseDetails> Cases);

public sealed record CaseResult(CaseDetails Case, EmployeeDetails Employee);

public sealed record TaskResult(TaskDetails Task);

public sealed record AuditListResult(IReadOnlyList<LifecycleAuditEntry> Entries);

public sealed record DepartmentInfo(long Id, string Name, string? ManagerName, string? OuDistinguishedName);

public sealed record DepartmentListResult(IReadOnlyList<DepartmentInfo> Departments);
