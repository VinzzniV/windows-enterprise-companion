using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.EmployeeLifecycle.Application;

namespace Wec.Modules.EmployeeLifecycle.Handlers;

public sealed record ListEmployeesRequest;

internal sealed class ListEmployeesHandler : IActionHandler<ListEmployeesRequest, EmployeeListResult>
{
    private readonly EmployeeLifecycleService _service;

    public ListEmployeesHandler(EmployeeLifecycleService service) => _service = service;

    public string Module => "employeelifecycle";
    public string Action => "listEmployees";

    public Task<Result<EmployeeListResult>> HandleAsync(ListEmployeesRequest payload, CancellationToken cancellationToken) =>
        _service.ListEmployeesAsync(cancellationToken);
}

public sealed record GetEmployeeRequest(long EmployeeId = 0);

internal sealed class GetEmployeeHandler : IActionHandler<GetEmployeeRequest, EmployeeDetailsResult>
{
    private readonly EmployeeLifecycleService _service;

    public GetEmployeeHandler(EmployeeLifecycleService service) => _service = service;

    public string Module => "employeelifecycle";
    public string Action => "getEmployee";

    public Task<Result<EmployeeDetailsResult>> HandleAsync(GetEmployeeRequest payload, CancellationToken cancellationToken) =>
        _service.GetEmployeeAsync(payload.EmployeeId, cancellationToken);
}

internal sealed class CreateEmployeeHandler : IActionHandler<EmployeeInput, EmployeeDetailsResult>
{
    private readonly EmployeeLifecycleService _service;

    public CreateEmployeeHandler(EmployeeLifecycleService service) => _service = service;

    public string Module => "employeelifecycle";
    public string Action => "createEmployee";

    public Task<Result<EmployeeDetailsResult>> HandleAsync(EmployeeInput payload, CancellationToken cancellationToken) =>
        _service.CreateEmployeeAsync(payload, cancellationToken);
}

public sealed record UpdateEmployeeRequest(
    long EmployeeId = 0,
    string FirstName = "",
    string LastName = "",
    string? Email = null,
    string? EmployeeNumber = null,
    string? Department = null,
    string? Title = null,
    string? Manager = null,
    string? SamAccountName = null,
    string? UserPrincipalName = null,
    string? DistinguishedName = null,
    DateOnly? EntryDate = null,
    string? Notes = null)
{
    public EmployeeInput ToInput() => new(
        FirstName, LastName, Email, EmployeeNumber, Department, Title, Manager,
        SamAccountName, UserPrincipalName, DistinguishedName, EntryDate, Notes);
}

internal sealed class UpdateEmployeeHandler : IActionHandler<UpdateEmployeeRequest, EmployeeDetailsResult>
{
    private readonly EmployeeLifecycleService _service;

    public UpdateEmployeeHandler(EmployeeLifecycleService service) => _service = service;

    public string Module => "employeelifecycle";
    public string Action => "updateEmployee";

    public Task<Result<EmployeeDetailsResult>> HandleAsync(UpdateEmployeeRequest payload, CancellationToken cancellationToken) =>
        _service.UpdateEmployeeAsync(payload.EmployeeId, payload.ToInput(), cancellationToken);
}
