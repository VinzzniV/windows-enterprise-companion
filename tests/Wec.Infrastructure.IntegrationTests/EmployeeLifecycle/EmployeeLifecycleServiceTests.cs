using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Infrastructure.IntegrationTests.Persistence;
using Wec.Infrastructure.Persistence;
using Wec.Modules.EmployeeLifecycle;
using Wec.Modules.EmployeeLifecycle.Application;
using Wec.Modules.EmployeeLifecycle.Domain;
using Wec.Modules.EmployeeLifecycle.Persistence;

namespace Wec.Infrastructure.IntegrationTests.EmployeeLifecycle;

public sealed class EmployeeLifecycleServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 7, 24);

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"wec-integration-{Guid.NewGuid():N}.db");

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private WecDbContext CreateContext() => IntegrationDbContextFactory.Create(_databasePath);

    private static EmployeeLifecycleService CreateService(WecDbContext dbContext) => new(
        dbContext,
        new FixedClock(Now),
        Microsoft.Extensions.Options.Options.Create(new EmployeeLifecycleOptions()));

    private static async Task<EmployeeDetails> CreateEmployeeAsync(EmployeeLifecycleService service, string lastName = "Muster")
    {
        Result<EmployeeDetailsResult> created = await service.CreateEmployeeAsync(
            new EmployeeInput(FirstName: "Max", LastName: lastName), CancellationToken.None);
        Assert.True(created.IsSuccess);
        return created.Value.Employee;
    }

    [Fact]
    public async Task Onboarding_FullFlow_CreatesTasksAndActivatesEmployee()
    {
        using WecDbContext dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();
        EmployeeLifecycleService service = CreateService(dbContext);

        EmployeeDetails employee = await CreateEmployeeAsync(service);
        Assert.Equal(EmployeeStatus.Planned, employee.Status);

        DateOnly entryDate = Today.AddDays(14);
        Result<CaseResult> started = await service.StartCaseAsync(
            employee.Id, CaseType.Onboarding, entryDate, "Neuzugang IT", CancellationToken.None);
        Assert.True(started.IsSuccess);
        Assert.Equal(EmployeeStatus.Onboarding, started.Value.Employee.Status);
        Assert.Equal(entryDate, started.Value.Employee.EntryDate);
        Assert.NotEmpty(started.Value.Case.Tasks);
        Assert.All(started.Value.Case.Tasks, task => Assert.Equal(LifecycleTaskStatus.Open, task.Status));

        // Completion is blocked while tasks are open
        Result<CaseResult> premature = await service.CompleteCaseAsync(started.Value.Case.Id, CancellationToken.None);
        Assert.True(premature.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, premature.Error!.Code);

        foreach (TaskDetails task in started.Value.Case.Tasks)
        {
            Result<TaskResult> updated = await service.UpdateTaskAsync(
                task.Id, LifecycleTaskStatus.Done, "vinzent", task.DueDate, CancellationToken.None);
            Assert.True(updated.IsSuccess);
        }

        Result<CaseResult> completed = await service.CompleteCaseAsync(started.Value.Case.Id, CancellationToken.None);
        Assert.True(completed.IsSuccess);
        Assert.Equal(CaseStatus.Completed, completed.Value.Case.Status);
        Assert.Equal(EmployeeStatus.Active, completed.Value.Employee.Status);

        Result<AuditListResult> audit = await service.ListAuditEntriesAsync(employee.Id, null, CancellationToken.None);
        Assert.True(audit.IsSuccess);
        List<string> eventTypes = audit.Value.Entries.Select(entry => entry.EventType).ToList();
        Assert.Contains("employee_created", eventTypes);
        Assert.Contains("case_started", eventTypes);
        Assert.Contains("task_status_changed", eventTypes);
        Assert.Contains("case_completed", eventTypes);
        Assert.Contains("employee_status_changed", eventTypes);
    }

    [Fact]
    public async Task StartCase_InvalidTypeOrSecondCase_IsRejected()
    {
        using WecDbContext dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();
        EmployeeLifecycleService service = CreateService(dbContext);
        EmployeeDetails employee = await CreateEmployeeAsync(service);

        // Offboarding requires an active employee
        Result<CaseResult> offboarding = await service.StartCaseAsync(
            employee.Id, CaseType.Offboarding, null, null, CancellationToken.None);
        Assert.True(offboarding.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, offboarding.Error!.Code);

        Result<CaseResult> onboarding = await service.StartCaseAsync(
            employee.Id, CaseType.Onboarding, null, null, CancellationToken.None);
        Assert.True(onboarding.IsSuccess);

        // Only one active case per employee
        Result<CaseResult> second = await service.StartCaseAsync(
            employee.Id, CaseType.Onboarding, null, null, CancellationToken.None);
        Assert.True(second.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, second.Error!.Code);
    }

    [Fact]
    public async Task Offboarding_Cancel_RestoresActiveStatusAndClearsExitDate()
    {
        using WecDbContext dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();
        dbContext.Set<EmployeeRecord>().Add(new EmployeeRecord
        {
            FirstName = "Erika",
            LastName = "Beispiel",
            Status = EmployeeStatus.Active,
            CreatedUtc = Now,
            UpdatedUtc = Now,
        });
        await dbContext.SaveChangesAsync();
        long employeeId = (await dbContext.Set<EmployeeRecord>().SingleAsync()).Id;
        EmployeeLifecycleService service = CreateService(dbContext);

        Result<CaseResult> started = await service.StartCaseAsync(
            employeeId, CaseType.Offboarding, Today.AddDays(30), null, CancellationToken.None);
        Assert.True(started.IsSuccess);
        Assert.Equal(EmployeeStatus.Offboarding, started.Value.Employee.Status);
        Assert.Equal(Today.AddDays(30), started.Value.Employee.ExitDate);

        Result<CaseResult> cancelled = await service.CancelCaseAsync(
            started.Value.Case.Id, "Mitarbeiter bleibt", CancellationToken.None);
        Assert.True(cancelled.IsSuccess);
        Assert.Equal(CaseStatus.Cancelled, cancelled.Value.Case.Status);
        Assert.Equal("Mitarbeiter bleibt", cancelled.Value.Case.CancelReason);
        Assert.Equal(EmployeeStatus.Active, cancelled.Value.Employee.Status);
        Assert.Null(cancelled.Value.Employee.ExitDate);
    }

    [Fact]
    public async Task Tasks_OnClosedCase_AreReadOnly()
    {
        using WecDbContext dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();
        EmployeeLifecycleService service = CreateService(dbContext);
        EmployeeDetails employee = await CreateEmployeeAsync(service);

        Result<CaseResult> started = await service.StartCaseAsync(
            employee.Id, CaseType.Onboarding, null, null, CancellationToken.None);
        Assert.True(started.IsSuccess);
        long taskId = started.Value.Case.Tasks[0].Id;

        Result<CaseResult> cancelled = await service.CancelCaseAsync(started.Value.Case.Id, null, CancellationToken.None);
        Assert.True(cancelled.IsSuccess);

        Result<TaskResult> update = await service.UpdateTaskAsync(
            taskId, LifecycleTaskStatus.Done, null, null, CancellationToken.None);
        Assert.True(update.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, update.Error!.Code);

        Result<TaskResult> note = await service.AddTaskNoteAsync(taskId, "zu spät", CancellationToken.None);
        Assert.True(note.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, note.Error!.Code);
    }

    [Fact]
    public async Task AddTaskNote_AppendsTimestampedLineAndAudits()
    {
        using WecDbContext dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();
        EmployeeLifecycleService service = CreateService(dbContext);
        EmployeeDetails employee = await CreateEmployeeAsync(service);

        Result<CaseResult> started = await service.StartCaseAsync(
            employee.Id, CaseType.Onboarding, null, null, CancellationToken.None);
        Assert.True(started.IsSuccess);
        long taskId = started.Value.Case.Tasks[0].Id;

        Result<TaskResult> first = await service.AddTaskNoteAsync(taskId, "Ticket erstellt", CancellationToken.None);
        Assert.True(first.IsSuccess);
        Result<TaskResult> second = await service.AddTaskNoteAsync(taskId, "Warte auf Hardware", CancellationToken.None);
        Assert.True(second.IsSuccess);

        string notes = second.Value.Task.Notes;
        Assert.Contains("Ticket erstellt", notes, StringComparison.Ordinal);
        Assert.Contains("Warte auf Hardware", notes, StringComparison.Ordinal);
        Assert.Equal(2, notes.Split('\n').Length);

        Result<AuditListResult> audit = await service.ListAuditEntriesAsync(employee.Id, null, CancellationToken.None);
        Assert.True(audit.IsSuccess);
        Assert.Equal(2, audit.Value.Entries.Count(entry => entry.EventType == "task_note_added"));
    }

    [Fact]
    public async Task ListEmployees_ReportsOpenAndOverdueTaskCounts()
    {
        using WecDbContext dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();
        EmployeeLifecycleService service = CreateService(dbContext);
        EmployeeDetails employee = await CreateEmployeeAsync(service);

        // Effective date in the past makes every prep task overdue
        Result<CaseResult> started = await service.StartCaseAsync(
            employee.Id, CaseType.Onboarding, Today.AddDays(-1), null, CancellationToken.None);
        Assert.True(started.IsSuccess);

        Result<EmployeeListResult> list = await service.ListEmployeesAsync(CancellationToken.None);
        Assert.True(list.IsSuccess);
        EmployeeSummary summary = Assert.Single(list.Value.Employees);
        Assert.Equal(CaseType.Onboarding, summary.ActiveCaseType);
        Assert.Equal(started.Value.Case.Tasks.Count, summary.OpenTaskCount);
        Assert.Equal(started.Value.Case.Tasks.Count, summary.OverdueTaskCount);
    }

    [Fact]
    public async Task Departments_UpsertByNameAndDelete()
    {
        using WecDbContext dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();
        EmployeeLifecycleService service = CreateService(dbContext);

        Result<DepartmentListResult> saved = await service.SaveDepartmentAsync(
            "IT", "Chef Admin", "OU=IT,DC=firma,DC=local", CancellationToken.None);
        Assert.True(saved.IsSuccess);
        DepartmentInfo department = Assert.Single(saved.Value.Departments);
        Assert.Equal("Chef Admin", department.ManagerName);

        // Same name (case-insensitive) updates instead of duplicating
        Result<DepartmentListResult> updated = await service.SaveDepartmentAsync(
            "it", "Neue Leitung", "OU=IT,DC=firma,DC=local", CancellationToken.None);
        Assert.True(updated.IsSuccess);
        DepartmentInfo replaced = Assert.Single(updated.Value.Departments);
        Assert.Equal("Neue Leitung", replaced.ManagerName);

        Result<DepartmentListResult> empty = await service.SaveDepartmentAsync(
            "  ", null, null, CancellationToken.None);
        Assert.True(empty.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, empty.Error!.Code);

        Result<DepartmentListResult> deleted = await service.DeleteDepartmentAsync(
            replaced.Id, CancellationToken.None);
        Assert.True(deleted.IsSuccess);
        Assert.Empty(deleted.Value.Departments);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
