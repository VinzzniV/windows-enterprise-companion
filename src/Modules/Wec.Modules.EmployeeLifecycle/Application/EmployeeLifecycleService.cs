using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.EmployeeLifecycle.Domain;
using Wec.Modules.EmployeeLifecycle.Persistence;

namespace Wec.Modules.EmployeeLifecycle.Application;

/// <summary>
/// All employee-lifecycle use cases. Every state change writes audit entries;
/// employee status is only ever changed through case start/complete/cancel.
/// </summary>
public sealed class EmployeeLifecycleService
{
    private readonly DbContext _dbContext;
    private readonly IClock _clock;
    private readonly EmployeeLifecycleOptions _options;

    public EmployeeLifecycleService(DbContext dbContext, IClock clock, IOptions<EmployeeLifecycleOptions> options)
    {
        _dbContext = dbContext;
        _clock = clock;
        _options = options.Value;
    }

    private static class Events
    {
        public const string EmployeeCreated = "employee_created";
        public const string EmployeeUpdated = "employee_updated";
        public const string EmployeeStatusChanged = "employee_status_changed";
        public const string CaseStarted = "case_started";
        public const string CaseCompleted = "case_completed";
        public const string CaseCancelled = "case_cancelled";
        public const string TaskStatusChanged = "task_status_changed";
        public const string TaskUpdated = "task_updated";
        public const string TaskNoteAdded = "task_note_added";
    }

    public async Task<Result<EmployeeListResult>> ListEmployeesAsync(CancellationToken cancellationToken)
    {
        List<EmployeeRecord> employees = await _dbContext.Set<EmployeeRecord>()
            .OrderBy(record => record.LastName).ThenBy(record => record.FirstName)
            .ToListAsync(cancellationToken);
        List<CaseRecord> activeCases = await _dbContext.Set<CaseRecord>()
            .Where(record => record.Status == CaseStatus.Active)
            .ToListAsync(cancellationToken);
        List<long> activeCaseIds = activeCases.Select(record => record.Id).ToList();
        List<CaseTaskRecord> openTasks = await _dbContext.Set<CaseTaskRecord>()
            .Where(record => activeCaseIds.Contains(record.CaseId)
                && record.Status != LifecycleTaskStatus.Done
                && record.Status != LifecycleTaskStatus.Skipped)
            .ToListAsync(cancellationToken);

        DateOnly today = Today;
        List<EmployeeSummary> summaries = employees.Select(employee =>
        {
            CaseRecord? activeCase = activeCases.Find(record => record.EmployeeId == employee.Id);
            List<CaseTaskRecord> caseOpenTasks = activeCase is null
                ? []
                : openTasks.Where(record => record.CaseId == activeCase.Id).ToList();
            return new EmployeeSummary(
                employee.Id, employee.FirstName, employee.LastName, employee.EmployeeNumber,
                employee.Department, employee.Title, employee.SamAccountName, employee.Status,
                employee.EntryDate, employee.ExitDate,
                activeCase?.Id, activeCase?.Type,
                caseOpenTasks.Count,
                caseOpenTasks.Count(record => record.DueDate is { } due && due < today));
        }).ToList();

        return Result.Success(new EmployeeListResult(summaries));
    }

    public async Task<Result<EmployeeDetailsResult>> GetEmployeeAsync(long employeeId, CancellationToken cancellationToken)
    {
        EmployeeRecord? employee = await _dbContext.Set<EmployeeRecord>()
            .FirstOrDefaultAsync(record => record.Id == employeeId, cancellationToken);
        if (employee is null)
        {
            return Result.Failure<EmployeeDetailsResult>(Error.NotFound($"Employee {employeeId} does not exist."));
        }

        return Result.Success(new EmployeeDetailsResult(
            MapEmployee(employee),
            await LoadCasesAsync(employeeId, cancellationToken)));
    }

    public async Task<Result<EmployeeDetailsResult>> CreateEmployeeAsync(EmployeeInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        input = Normalize(input);
        if (input.FirstName.Length == 0 || input.LastName.Length == 0)
        {
            return Result.Failure<EmployeeDetailsResult>(
                new Error(ErrorCode.InvalidRequest, "First name and last name are required."));
        }

        DateTimeOffset now = _clock.UtcNow;
        var employee = new EmployeeRecord
        {
            FirstName = input.FirstName,
            LastName = input.LastName,
            Email = input.Email,
            EmployeeNumber = input.EmployeeNumber,
            Department = input.Department,
            Title = input.Title,
            Manager = input.Manager,
            SamAccountName = input.SamAccountName,
            UserPrincipalName = input.UserPrincipalName,
            DistinguishedName = input.DistinguishedName,
            Status = EmployeeStatus.Planned,
            EntryDate = input.EntryDate,
            Notes = input.Notes,
            CreatedUtc = now,
            UpdatedUtc = now,
        };
        _dbContext.Set<EmployeeRecord>().Add(employee);
        await _dbContext.SaveChangesAsync(cancellationToken);

        AddAudit(now, employee.Id, null, null, Events.EmployeeCreated,
            detail: $"{employee.FirstName} {employee.LastName}");
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new EmployeeDetailsResult(MapEmployee(employee), []));
    }

    public async Task<Result<EmployeeDetailsResult>> UpdateEmployeeAsync(
        long employeeId, EmployeeInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        input = Normalize(input);
        if (input.FirstName.Length == 0 || input.LastName.Length == 0)
        {
            return Result.Failure<EmployeeDetailsResult>(
                new Error(ErrorCode.InvalidRequest, "First name and last name are required."));
        }

        EmployeeRecord? employee = await _dbContext.Set<EmployeeRecord>()
            .FirstOrDefaultAsync(record => record.Id == employeeId, cancellationToken);
        if (employee is null)
        {
            return Result.Failure<EmployeeDetailsResult>(Error.NotFound($"Employee {employeeId} does not exist."));
        }

        List<string> changedFields = [];
        if (employee.FirstName != input.FirstName) { changedFields.Add("firstName"); employee.FirstName = input.FirstName; }
        if (employee.LastName != input.LastName) { changedFields.Add("lastName"); employee.LastName = input.LastName; }
        if (employee.Email != input.Email) { changedFields.Add("email"); employee.Email = input.Email; }
        if (employee.EmployeeNumber != input.EmployeeNumber) { changedFields.Add("employeeNumber"); employee.EmployeeNumber = input.EmployeeNumber; }
        if (employee.Department != input.Department) { changedFields.Add("department"); employee.Department = input.Department; }
        if (employee.Title != input.Title) { changedFields.Add("title"); employee.Title = input.Title; }
        if (employee.Manager != input.Manager) { changedFields.Add("manager"); employee.Manager = input.Manager; }
        if (employee.SamAccountName != input.SamAccountName) { changedFields.Add("samAccountName"); employee.SamAccountName = input.SamAccountName; }
        if (employee.UserPrincipalName != input.UserPrincipalName) { changedFields.Add("userPrincipalName"); employee.UserPrincipalName = input.UserPrincipalName; }
        if (employee.DistinguishedName != input.DistinguishedName) { changedFields.Add("distinguishedName"); employee.DistinguishedName = input.DistinguishedName; }
        if (employee.EntryDate != input.EntryDate) { changedFields.Add("entryDate"); employee.EntryDate = input.EntryDate; }
        if (employee.Notes != input.Notes) { changedFields.Add("notes"); employee.Notes = input.Notes; }

        if (changedFields.Count > 0)
        {
            DateTimeOffset now = _clock.UtcNow;
            employee.UpdatedUtc = now;
            AddAudit(now, employee.Id, null, null, Events.EmployeeUpdated,
                detail: string.Join(", ", changedFields));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(new EmployeeDetailsResult(
            MapEmployee(employee),
            await LoadCasesAsync(employeeId, cancellationToken)));
    }

    public async Task<Result<CaseResult>> StartCaseAsync(
        long employeeId, CaseType caseType, DateOnly? effectiveDate, string? note, CancellationToken cancellationToken)
    {
        EmployeeRecord? employee = await _dbContext.Set<EmployeeRecord>()
            .FirstOrDefaultAsync(record => record.Id == employeeId, cancellationToken);
        if (employee is null)
        {
            return Result.Failure<CaseResult>(Error.NotFound($"Employee {employeeId} does not exist."));
        }

        bool hasActiveCase = await _dbContext.Set<CaseRecord>()
            .AnyAsync(record => record.EmployeeId == employeeId && record.Status == CaseStatus.Active, cancellationToken);
        if (hasActiveCase)
        {
            return Result.Failure<CaseResult>(new Error(
                ErrorCode.InvalidRequest, "The employee already has an active case. Complete or cancel it first."));
        }

        if (!LifecycleRules.CanStartCase(employee.Status, caseType))
        {
            return Result.Failure<CaseResult>(new Error(
                ErrorCode.InvalidRequest,
                $"A {caseType} case cannot be started while the employee status is {employee.Status}."));
        }

        DateTimeOffset now = _clock.UtcNow;
        var lifecycleCase = new CaseRecord
        {
            EmployeeId = employeeId,
            Type = caseType,
            Status = CaseStatus.Active,
            EffectiveDate = effectiveDate,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedUtc = now,
        };

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        _dbContext.Set<CaseRecord>().Add(lifecycleCase);
        await _dbContext.SaveChangesAsync(cancellationToken);

        IReadOnlyList<ChecklistItem> checklist = DefaultChecklists.For(caseType, _options.AdAccountDeletionRetentionDays);
        List<CaseTaskRecord> tasks = checklist.Select((item, index) => new CaseTaskRecord
        {
            CaseId = lifecycleCase.Id,
            Title = item.Title,
            Area = item.Area,
            Status = LifecycleTaskStatus.Open,
            DueDate = DefaultChecklists.DueDateFor(item, effectiveDate),
            SortOrder = index,
            CreatedUtc = now,
        }).ToList();
        _dbContext.Set<CaseTaskRecord>().AddRange(tasks);

        EmployeeStatus oldStatus = employee.Status;
        employee.Status = LifecycleRules.StatusWhileCaseActive(caseType);
        employee.UpdatedUtc = now;
        if (effectiveDate is not null && caseType == CaseType.Onboarding)
        {
            employee.EntryDate = effectiveDate;
        }
        if (effectiveDate is not null && caseType == CaseType.Offboarding)
        {
            employee.ExitDate = effectiveDate;
        }

        AddAudit(now, employeeId, lifecycleCase.Id, null, Events.CaseStarted,
            newValue: caseType.ToString(), detail: lifecycleCase.Note);
        AddAudit(now, employeeId, lifecycleCase.Id, null, Events.EmployeeStatusChanged,
            oldValue: oldStatus.ToString(), newValue: employee.Status.ToString());
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Success(new CaseResult(MapCase(lifecycleCase, tasks), MapEmployee(employee)));
    }

    public async Task<Result<CaseResult>> GetCaseAsync(long caseId, CancellationToken cancellationToken)
    {
        (CaseRecord? lifecycleCase, EmployeeRecord? employee, List<CaseTaskRecord> tasks) =
            await LoadCaseAsync(caseId, cancellationToken);
        if (lifecycleCase is null || employee is null)
        {
            return Result.Failure<CaseResult>(Error.NotFound($"Case {caseId} does not exist."));
        }

        return Result.Success(new CaseResult(MapCase(lifecycleCase, tasks), MapEmployee(employee)));
    }

    public async Task<Result<CaseResult>> CompleteCaseAsync(long caseId, CancellationToken cancellationToken)
    {
        (CaseRecord? lifecycleCase, EmployeeRecord? employee, List<CaseTaskRecord> tasks) =
            await LoadCaseAsync(caseId, cancellationToken);
        if (lifecycleCase is null || employee is null)
        {
            return Result.Failure<CaseResult>(Error.NotFound($"Case {caseId} does not exist."));
        }
        if (lifecycleCase.Status != CaseStatus.Active)
        {
            return Result.Failure<CaseResult>(new Error(
                ErrorCode.InvalidRequest, $"Case {caseId} is already {lifecycleCase.Status}."));
        }

        int unfinishedTasks = tasks.Count(task => !LifecycleRules.IsTerminal(task.Status));
        if (unfinishedTasks > 0)
        {
            return Result.Failure<CaseResult>(new Error(
                ErrorCode.InvalidRequest,
                $"{unfinishedTasks} task(s) are neither done nor skipped. Finish them before completing the case."));
        }

        DateTimeOffset now = _clock.UtcNow;
        lifecycleCase.Status = CaseStatus.Completed;
        lifecycleCase.ClosedUtc = now;

        EmployeeStatus oldStatus = employee.Status;
        employee.Status = LifecycleRules.StatusAfterCompletion(lifecycleCase.Type);
        employee.UpdatedUtc = now;
        DateOnly today = Today;
        if (lifecycleCase.Type == CaseType.Onboarding)
        {
            employee.EntryDate ??= lifecycleCase.EffectiveDate ?? today;
        }
        if (lifecycleCase.Type == CaseType.Offboarding)
        {
            employee.ExitDate ??= lifecycleCase.EffectiveDate ?? today;
        }

        AddAudit(now, employee.Id, caseId, null, Events.CaseCompleted, newValue: lifecycleCase.Type.ToString());
        AddAudit(now, employee.Id, caseId, null, Events.EmployeeStatusChanged,
            oldValue: oldStatus.ToString(), newValue: employee.Status.ToString());
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new CaseResult(MapCase(lifecycleCase, tasks), MapEmployee(employee)));
    }

    public async Task<Result<CaseResult>> CancelCaseAsync(long caseId, string? reason, CancellationToken cancellationToken)
    {
        (CaseRecord? lifecycleCase, EmployeeRecord? employee, List<CaseTaskRecord> tasks) =
            await LoadCaseAsync(caseId, cancellationToken);
        if (lifecycleCase is null || employee is null)
        {
            return Result.Failure<CaseResult>(Error.NotFound($"Case {caseId} does not exist."));
        }
        if (lifecycleCase.Status != CaseStatus.Active)
        {
            return Result.Failure<CaseResult>(new Error(
                ErrorCode.InvalidRequest, $"Case {caseId} is already {lifecycleCase.Status}."));
        }

        DateTimeOffset now = _clock.UtcNow;
        lifecycleCase.Status = CaseStatus.Cancelled;
        lifecycleCase.ClosedUtc = now;
        lifecycleCase.CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        EmployeeStatus oldStatus = employee.Status;
        employee.Status = LifecycleRules.StatusAfterCancellation(lifecycleCase.Type);
        employee.UpdatedUtc = now;
        if (lifecycleCase.Type == CaseType.Offboarding)
        {
            employee.ExitDate = null;
        }

        AddAudit(now, employee.Id, caseId, null, Events.CaseCancelled,
            oldValue: CaseStatus.Active.ToString(), newValue: CaseStatus.Cancelled.ToString(),
            detail: lifecycleCase.CancelReason);
        AddAudit(now, employee.Id, caseId, null, Events.EmployeeStatusChanged,
            oldValue: oldStatus.ToString(), newValue: employee.Status.ToString());
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new CaseResult(MapCase(lifecycleCase, tasks), MapEmployee(employee)));
    }

    public async Task<Result<TaskResult>> UpdateTaskAsync(
        long taskId, LifecycleTaskStatus status, string? assignee, DateOnly? dueDate, CancellationToken cancellationToken)
    {
        (CaseTaskRecord? task, CaseRecord? lifecycleCase) = await LoadTaskAsync(taskId, cancellationToken);
        if (task is null || lifecycleCase is null)
        {
            return Result.Failure<TaskResult>(Error.NotFound($"Task {taskId} does not exist."));
        }
        if (lifecycleCase.Status != CaseStatus.Active)
        {
            return Result.Failure<TaskResult>(new Error(
                ErrorCode.InvalidRequest, $"Tasks of a {lifecycleCase.Status} case can no longer be changed."));
        }

        DateTimeOffset now = _clock.UtcNow;
        assignee = string.IsNullOrWhiteSpace(assignee) ? null : assignee.Trim();

        if (task.Status != status)
        {
            AddAudit(now, lifecycleCase.EmployeeId, lifecycleCase.Id, taskId, Events.TaskStatusChanged,
                oldValue: task.Status.ToString(), newValue: status.ToString(), detail: task.Title);
            task.Status = status;
            task.CompletedUtc = LifecycleRules.IsTerminal(status) ? now : null;
        }

        List<string> changedFields = [];
        if (task.Assignee != assignee) { changedFields.Add("assignee"); task.Assignee = assignee; }
        if (task.DueDate != dueDate) { changedFields.Add("dueDate"); task.DueDate = dueDate; }
        if (changedFields.Count > 0)
        {
            AddAudit(now, lifecycleCase.EmployeeId, lifecycleCase.Id, taskId, Events.TaskUpdated,
                detail: $"{task.Title}: {string.Join(", ", changedFields)}");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(new TaskResult(MapTask(task, caseActive: true, Today)));
    }

    public async Task<Result<TaskResult>> AddTaskNoteAsync(long taskId, string note, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return Result.Failure<TaskResult>(new Error(ErrorCode.InvalidRequest, "The note must not be empty."));
        }

        (CaseTaskRecord? task, CaseRecord? lifecycleCase) = await LoadTaskAsync(taskId, cancellationToken);
        if (task is null || lifecycleCase is null)
        {
            return Result.Failure<TaskResult>(Error.NotFound($"Task {taskId} does not exist."));
        }
        if (lifecycleCase.Status != CaseStatus.Active)
        {
            return Result.Failure<TaskResult>(new Error(
                ErrorCode.InvalidRequest, $"Tasks of a {lifecycleCase.Status} case can no longer be changed."));
        }

        DateTimeOffset now = _clock.UtcNow;
        string header = now.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        string line = $"[{header} {Environment.UserName}] {note.Trim()}";
        task.Notes = task.Notes.Length == 0 ? line : $"{task.Notes}\n{line}";

        AddAudit(now, lifecycleCase.EmployeeId, lifecycleCase.Id, taskId, Events.TaskNoteAdded,
            detail: note.Trim());
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(new TaskResult(MapTask(task, caseActive: true, Today)));
    }

    public async Task<Result<AuditListResult>> ListAuditEntriesAsync(
        long? employeeId, int? limit, CancellationToken cancellationToken)
    {
        IQueryable<LifecycleAuditRecord> query = _dbContext.Set<LifecycleAuditRecord>();
        if (employeeId is not null)
        {
            query = query.Where(record => record.EmployeeId == employeeId);
        }

        List<LifecycleAuditRecord> records = await query
            .OrderByDescending(record => record.TimestampUtc).ThenByDescending(record => record.Id)
            .Take(limit ?? _options.AuditHistoryLimit)
            .ToListAsync(cancellationToken);

        return Result.Success(new AuditListResult(records.Select(record => new LifecycleAuditEntry(
            record.Id, record.TimestampUtc, record.UserName, record.EmployeeId, record.CaseId,
            record.TaskId, record.EventType, record.OldValue, record.NewValue, record.Detail)).ToList()));
    }

    public async Task<Result<DepartmentListResult>> ListDepartmentsAsync(CancellationToken cancellationToken) =>
        Result.Success(await LoadDepartmentListAsync(cancellationToken));

    /// <summary>Upsert by name (case-insensitive) — the catalog stays free of duplicate departments.</summary>
    public async Task<Result<DepartmentListResult>> SaveDepartmentAsync(
        string name, string? managerName, string? ouDistinguishedName, CancellationToken cancellationToken)
    {
        string? cleanedName = Clean(name);
        if (cleanedName is null)
        {
            return Result.Failure<DepartmentListResult>(
                new Error(ErrorCode.InvalidRequest, "The department name must not be empty."));
        }

        List<DepartmentRecord> existing = await _dbContext.Set<DepartmentRecord>().ToListAsync(cancellationToken);
        DepartmentRecord? department = existing.Find(
            record => string.Equals(record.Name, cleanedName, StringComparison.OrdinalIgnoreCase));
        if (department is null)
        {
            department = new DepartmentRecord { Name = cleanedName };
            _dbContext.Set<DepartmentRecord>().Add(department);
        }

        department.Name = cleanedName;
        department.ManagerName = Clean(managerName);
        department.OuDistinguishedName = Clean(ouDistinguishedName);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(await LoadDepartmentListAsync(cancellationToken));
    }

    public async Task<Result<DepartmentListResult>> DeleteDepartmentAsync(
        long departmentId, CancellationToken cancellationToken)
    {
        await _dbContext.Set<DepartmentRecord>()
            .Where(record => record.Id == departmentId)
            .ExecuteDeleteAsync(cancellationToken);
        return Result.Success(await LoadDepartmentListAsync(cancellationToken));
    }

    private async Task<DepartmentListResult> LoadDepartmentListAsync(CancellationToken cancellationToken)
    {
        List<DepartmentRecord> departments = await _dbContext.Set<DepartmentRecord>()
            .OrderBy(record => record.Name)
            .ToListAsync(cancellationToken);
        return new DepartmentListResult(departments
            .Select(record => new DepartmentInfo(record.Id, record.Name, record.ManagerName, record.OuDistinguishedName))
            .ToList());
    }

    private DateOnly Today => DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

    private void AddAudit(
        DateTimeOffset timestamp, long employeeId, long? caseId, long? taskId, string eventType,
        string? oldValue = null, string? newValue = null, string? detail = null)
    {
        _dbContext.Set<LifecycleAuditRecord>().Add(new LifecycleAuditRecord
        {
            TimestampUtc = timestamp,
            UserName = Environment.UserName,
            EmployeeId = employeeId,
            CaseId = caseId,
            TaskId = taskId,
            EventType = eventType,
            OldValue = oldValue,
            NewValue = newValue,
            Detail = detail,
        });
    }

    private async Task<IReadOnlyList<CaseDetails>> LoadCasesAsync(long employeeId, CancellationToken cancellationToken)
    {
        List<CaseRecord> cases = await _dbContext.Set<CaseRecord>()
            .Where(record => record.EmployeeId == employeeId)
            .OrderByDescending(record => record.CreatedUtc).ThenByDescending(record => record.Id)
            .ToListAsync(cancellationToken);
        List<long> caseIds = cases.Select(record => record.Id).ToList();
        List<CaseTaskRecord> tasks = await _dbContext.Set<CaseTaskRecord>()
            .Where(record => caseIds.Contains(record.CaseId))
            .ToListAsync(cancellationToken);
        return cases
            .Select(record => MapCase(record, tasks.Where(task => task.CaseId == record.Id)))
            .ToList();
    }

    private async Task<(CaseRecord? Case, EmployeeRecord? Employee, List<CaseTaskRecord> Tasks)> LoadCaseAsync(
        long caseId, CancellationToken cancellationToken)
    {
        CaseRecord? lifecycleCase = await _dbContext.Set<CaseRecord>()
            .FirstOrDefaultAsync(record => record.Id == caseId, cancellationToken);
        if (lifecycleCase is null)
        {
            return (null, null, []);
        }

        EmployeeRecord? employee = await _dbContext.Set<EmployeeRecord>()
            .FirstOrDefaultAsync(record => record.Id == lifecycleCase.EmployeeId, cancellationToken);
        List<CaseTaskRecord> tasks = await _dbContext.Set<CaseTaskRecord>()
            .Where(record => record.CaseId == caseId)
            .ToListAsync(cancellationToken);
        return (lifecycleCase, employee, tasks);
    }

    private async Task<(CaseTaskRecord? Task, CaseRecord? Case)> LoadTaskAsync(
        long taskId, CancellationToken cancellationToken)
    {
        CaseTaskRecord? task = await _dbContext.Set<CaseTaskRecord>()
            .FirstOrDefaultAsync(record => record.Id == taskId, cancellationToken);
        if (task is null)
        {
            return (null, null);
        }

        CaseRecord? lifecycleCase = await _dbContext.Set<CaseRecord>()
            .FirstOrDefaultAsync(record => record.Id == task.CaseId, cancellationToken);
        return (task, lifecycleCase);
    }

    private CaseDetails MapCase(CaseRecord lifecycleCase, IEnumerable<CaseTaskRecord> tasks)
    {
        DateOnly today = Today;
        bool caseActive = lifecycleCase.Status == CaseStatus.Active;
        return new CaseDetails(
            lifecycleCase.Id, lifecycleCase.EmployeeId, lifecycleCase.Type, lifecycleCase.Status,
            lifecycleCase.EffectiveDate, lifecycleCase.Note, lifecycleCase.CancelReason,
            lifecycleCase.CreatedUtc, lifecycleCase.ClosedUtc,
            tasks.OrderBy(task => task.SortOrder).ThenBy(task => task.Id)
                .Select(task => MapTask(task, caseActive, today)).ToList());
    }

    private static TaskDetails MapTask(CaseTaskRecord task, bool caseActive, DateOnly today) => new(
        task.Id, task.CaseId, task.Title, task.Area, task.Status, task.DueDate, task.Assignee,
        task.Notes, task.SortOrder,
        IsOverdue: caseActive && !LifecycleRules.IsTerminal(task.Status) && task.DueDate is { } due && due < today);

    private static EmployeeDetails MapEmployee(EmployeeRecord employee) => new(
        employee.Id, employee.FirstName, employee.LastName, employee.Email, employee.EmployeeNumber,
        employee.Department, employee.Title, employee.Manager, employee.SamAccountName,
        employee.UserPrincipalName, employee.DistinguishedName, employee.Status,
        employee.EntryDate, employee.ExitDate, employee.Notes, employee.CreatedUtc, employee.UpdatedUtc);

    private static EmployeeInput Normalize(EmployeeInput input) => new(
        Clean(input.FirstName) ?? string.Empty,
        Clean(input.LastName) ?? string.Empty,
        Clean(input.Email),
        Clean(input.EmployeeNumber),
        Clean(input.Department),
        Clean(input.Title),
        Clean(input.Manager),
        Clean(input.SamAccountName),
        Clean(input.UserPrincipalName),
        Clean(input.DistinguishedName),
        input.EntryDate,
        Clean(input.Notes));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
