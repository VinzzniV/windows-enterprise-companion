using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.EmployeeLifecycle.Application;

namespace Wec.Modules.EmployeeLifecycle.Handlers;

public sealed record ListAuditEntriesRequest(long? EmployeeId = null, int? Limit = null);

internal sealed class ListAuditEntriesHandler : IActionHandler<ListAuditEntriesRequest, AuditListResult>
{
    private readonly EmployeeLifecycleService _service;

    public ListAuditEntriesHandler(EmployeeLifecycleService service) => _service = service;

    public string Module => "employeelifecycle";
    public string Action => "listAuditEntries";

    public Task<Result<AuditListResult>> HandleAsync(ListAuditEntriesRequest payload, CancellationToken cancellationToken) =>
        _service.ListAuditEntriesAsync(payload.EmployeeId, payload.Limit, cancellationToken);
}
