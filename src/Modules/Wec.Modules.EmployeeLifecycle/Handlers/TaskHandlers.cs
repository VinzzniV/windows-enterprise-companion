using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.EmployeeLifecycle.Application;
using Wec.Modules.EmployeeLifecycle.Domain;

namespace Wec.Modules.EmployeeLifecycle.Handlers;

public sealed record UpdateTaskRequest(
    long TaskId = 0,
    LifecycleTaskStatus Status = LifecycleTaskStatus.Open,
    string? Assignee = null,
    DateOnly? DueDate = null);

internal sealed class UpdateTaskHandler : IActionHandler<UpdateTaskRequest, TaskResult>
{
    private readonly EmployeeLifecycleService _service;

    public UpdateTaskHandler(EmployeeLifecycleService service) => _service = service;

    public string Module => "employeelifecycle";
    public string Action => "updateTask";

    public Task<Result<TaskResult>> HandleAsync(UpdateTaskRequest payload, CancellationToken cancellationToken) =>
        _service.UpdateTaskAsync(payload.TaskId, payload.Status, payload.Assignee, payload.DueDate, cancellationToken);
}

public sealed record AddTaskNoteRequest(long TaskId = 0, string Note = "");

internal sealed class AddTaskNoteHandler : IActionHandler<AddTaskNoteRequest, TaskResult>
{
    private readonly EmployeeLifecycleService _service;

    public AddTaskNoteHandler(EmployeeLifecycleService service) => _service = service;

    public string Module => "employeelifecycle";
    public string Action => "addTaskNote";

    public Task<Result<TaskResult>> HandleAsync(AddTaskNoteRequest payload, CancellationToken cancellationToken) =>
        _service.AddTaskNoteAsync(payload.TaskId, payload.Note, cancellationToken);
}
