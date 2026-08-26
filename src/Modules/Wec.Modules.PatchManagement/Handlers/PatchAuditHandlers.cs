using Microsoft.Extensions.Options;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.PatchManagement.Persistence;

namespace Wec.Modules.PatchManagement.Handlers;

public sealed record GetAuditLogRequest(int? Limit = null);
public sealed record AuditLogResult(IReadOnlyList<PatchAuditEntry> Entries);

internal sealed class GetAuditLogHandler : IActionHandler<GetAuditLogRequest, AuditLogResult>
{
    private readonly IPatchAuditRepository _auditRepository;
    private readonly PatchManagementOptions _options;

    public GetAuditLogHandler(IPatchAuditRepository auditRepository, IOptions<PatchManagementOptions> options)
    {
        _auditRepository = auditRepository;
        _options = options.Value;
    }

    public string Module => "patchmanagement";
    public string Action => "getAuditLog";

    public async Task<Result<AuditLogResult>> HandleAsync(
        GetAuditLogRequest payload,
        CancellationToken cancellationToken) =>
        Result.Success(new AuditLogResult(await _auditRepository.ListAsync(
            payload.Limit is > 0 ? payload.Limit.Value : _options.AuditHistoryLimit,
            cancellationToken)));
}
