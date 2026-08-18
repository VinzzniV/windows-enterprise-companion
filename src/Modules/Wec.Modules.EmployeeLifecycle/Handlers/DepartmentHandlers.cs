using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.EmployeeLifecycle.Application;

namespace Wec.Modules.EmployeeLifecycle.Handlers;

public sealed record ListDepartmentsRequest;

internal sealed class ListDepartmentsHandler : IActionHandler<ListDepartmentsRequest, DepartmentListResult>
{
    private readonly EmployeeLifecycleService _service;

    public ListDepartmentsHandler(EmployeeLifecycleService service) => _service = service;

    public string Module => "employeelifecycle";
    public string Action => "listDepartments";

    public Task<Result<DepartmentListResult>> HandleAsync(ListDepartmentsRequest payload, CancellationToken cancellationToken) =>
        _service.ListDepartmentsAsync(cancellationToken);
}

public sealed record SaveDepartmentRequest(
    string Name = "",
    string? ManagerName = null,
    string? OuDistinguishedName = null);

internal sealed class SaveDepartmentHandler : IActionHandler<SaveDepartmentRequest, DepartmentListResult>
{
    private readonly EmployeeLifecycleService _service;

    public SaveDepartmentHandler(EmployeeLifecycleService service) => _service = service;

    public string Module => "employeelifecycle";
    public string Action => "saveDepartment";

    public Task<Result<DepartmentListResult>> HandleAsync(SaveDepartmentRequest payload, CancellationToken cancellationToken) =>
        _service.SaveDepartmentAsync(payload.Name, payload.ManagerName, payload.OuDistinguishedName, cancellationToken);
}

public sealed record DeleteDepartmentRequest(long DepartmentId = 0);

internal sealed class DeleteDepartmentHandler : IActionHandler<DeleteDepartmentRequest, DepartmentListResult>
{
    private readonly EmployeeLifecycleService _service;

    public DeleteDepartmentHandler(EmployeeLifecycleService service) => _service = service;

    public string Module => "employeelifecycle";
    public string Action => "deleteDepartment";

    public Task<Result<DepartmentListResult>> HandleAsync(DeleteDepartmentRequest payload, CancellationToken cancellationToken) =>
        _service.DeleteDepartmentAsync(payload.DepartmentId, cancellationToken);
}
