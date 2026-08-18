using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.EmployeeLifecycle.Application;

namespace Wec.Modules.EmployeeLifecycle.Handlers;

internal sealed class GetItHygieneHandler : IActionHandler<ItHygieneRequest, ItHygieneResult>
{
    private readonly ItHygieneService _service;

    public GetItHygieneHandler(ItHygieneService service)
    {
        _service = service;
    }

    public string Module => "employeelifecycle";

    public string Action => "getHygiene";

    public Task<Result<ItHygieneResult>> HandleAsync(
        ItHygieneRequest payload,
        CancellationToken cancellationToken) =>
        _service.LoadAsync(payload, cancellationToken);
}
