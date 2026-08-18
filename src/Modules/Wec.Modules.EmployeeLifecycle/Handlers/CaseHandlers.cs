using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.EmployeeLifecycle.Application;
using Wec.Modules.EmployeeLifecycle.Domain;

namespace Wec.Modules.EmployeeLifecycle.Handlers;

public sealed record StartCaseRequest(
    long EmployeeId = 0,
    CaseType Type = CaseType.Onboarding,
    DateOnly? EffectiveDate = null,
    string? Note = null);

internal sealed class StartCaseHandler : IActionHandler<StartCaseRequest, CaseResult>
{
    private readonly EmployeeLifecycleService _service;

    public StartCaseHandler(EmployeeLifecycleService service) => _service = service;

    public string Module => "employeelifecycle";
    public string Action => "startCase";

    public Task<Result<CaseResult>> HandleAsync(StartCaseRequest payload, CancellationToken cancellationToken) =>
        _service.StartCaseAsync(payload.EmployeeId, payload.Type, payload.EffectiveDate, payload.Note, cancellationToken);
}

public sealed record GetCaseRequest(long CaseId = 0);

internal sealed class GetCaseHandler : IActionHandler<GetCaseRequest, CaseResult>
{
    private readonly EmployeeLifecycleService _service;

    public GetCaseHandler(EmployeeLifecycleService service) => _service = service;

    public string Module => "employeelifecycle";
    public string Action => "getCase";

    public Task<Result<CaseResult>> HandleAsync(GetCaseRequest payload, CancellationToken cancellationToken) =>
        _service.GetCaseAsync(payload.CaseId, cancellationToken);
}

public sealed record CompleteCaseRequest(long CaseId = 0);

internal sealed class CompleteCaseHandler : IActionHandler<CompleteCaseRequest, CaseResult>
{
    private readonly EmployeeLifecycleService _service;

    public CompleteCaseHandler(EmployeeLifecycleService service) => _service = service;

    public string Module => "employeelifecycle";
    public string Action => "completeCase";

    public Task<Result<CaseResult>> HandleAsync(CompleteCaseRequest payload, CancellationToken cancellationToken) =>
        _service.CompleteCaseAsync(payload.CaseId, cancellationToken);
}

public sealed record CancelCaseRequest(long CaseId = 0, string? Reason = null);

internal sealed class CancelCaseHandler : IActionHandler<CancelCaseRequest, CaseResult>
{
    private readonly EmployeeLifecycleService _service;

    public CancelCaseHandler(EmployeeLifecycleService service) => _service = service;

    public string Module => "employeelifecycle";
    public string Action => "cancelCase";

    public Task<Result<CaseResult>> HandleAsync(CancelCaseRequest payload, CancellationToken cancellationToken) =>
        _service.CancelCaseAsync(payload.CaseId, payload.Reason, cancellationToken);
}
