using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
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
        GetAuditLogRequest payload, CancellationToken cancellationToken) =>
        Result.Success(new AuditLogResult(await _auditRepository.ListAsync(
            payload.Limit is > 0 ? payload.Limit.Value : _options.AuditHistoryLimit,
            cancellationToken)));
}

public sealed record ListMappingsRequest;

public sealed record MappingsResult(IReadOnlyList<ProductMapping> Mappings);

internal sealed class ListMappingsHandler : IActionHandler<ListMappingsRequest, MappingsResult>
{
    private readonly IPatchMappingRepository _mappingRepository;

    public ListMappingsHandler(IPatchMappingRepository mappingRepository)
    {
        _mappingRepository = mappingRepository;
    }

    public string Module => "patchmanagement";

    public string Action => "listMappings";

    public async Task<Result<MappingsResult>> HandleAsync(
        ListMappingsRequest payload, CancellationToken cancellationToken) =>
        Result.Success(new MappingsResult(await _mappingRepository.ListAsync(cancellationToken)));
}

public sealed record SaveMappingRequest(string SoftwareName, string OpsiProductId);

internal sealed class SaveMappingHandler : IActionHandler<SaveMappingRequest, MappingsResult>
{
    private readonly IPatchMappingRepository _mappingRepository;
    private readonly IPatchAuditRepository _auditRepository;
    private readonly IClock _clock;

    public SaveMappingHandler(
        IPatchMappingRepository mappingRepository,
        IPatchAuditRepository auditRepository,
        IClock clock)
    {
        _mappingRepository = mappingRepository;
        _auditRepository = auditRepository;
        _clock = clock;
    }

    public string Module => "patchmanagement";

    public string Action => "saveMapping";

    public async Task<Result<MappingsResult>> HandleAsync(
        SaveMappingRequest payload, CancellationToken cancellationToken)
    {
        string softwareName = payload.SoftwareName?.Trim() ?? string.Empty;
        string productId = payload.OpsiProductId?.Trim() ?? string.Empty;
        if (softwareName.Length == 0 || productId.Length == 0)
        {
            return Result.Failure<MappingsResult>(new Error(
                ErrorCode.InvalidRequest, "Both the software name and the opsi product id are required."));
        }

        await _mappingRepository.UpsertAsync(softwareName, productId, cancellationToken);
        await _auditRepository.AddAsync(
            new PatchAuditEntry(
                Id: 0, _clock.UtcNow, Environment.UserName, "MAPPING_SAVED",
                productId, DepotId: null, TargetClients: [],
                PreviewJson: $"{{\"softwareName\":{System.Text.Json.JsonSerializer.Serialize(softwareName)}}}",
                "SUCCESS", ErrorMessage: null),
            cancellationToken);
        return Result.Success(new MappingsResult(await _mappingRepository.ListAsync(cancellationToken)));
    }
}

public sealed record DeleteMappingRequest(string SoftwareName);

internal sealed class DeleteMappingHandler : IActionHandler<DeleteMappingRequest, MappingsResult>
{
    private readonly IPatchMappingRepository _mappingRepository;
    private readonly IPatchAuditRepository _auditRepository;
    private readonly IClock _clock;

    public DeleteMappingHandler(
        IPatchMappingRepository mappingRepository,
        IPatchAuditRepository auditRepository,
        IClock clock)
    {
        _mappingRepository = mappingRepository;
        _auditRepository = auditRepository;
        _clock = clock;
    }

    public string Module => "patchmanagement";

    public string Action => "deleteMapping";

    public async Task<Result<MappingsResult>> HandleAsync(
        DeleteMappingRequest payload, CancellationToken cancellationToken)
    {
        string softwareName = payload.SoftwareName?.Trim() ?? string.Empty;
        if (softwareName.Length == 0)
        {
            return Result.Failure<MappingsResult>(new Error(
                ErrorCode.InvalidRequest, "The software name is required."));
        }

        await _mappingRepository.DeleteAsync(softwareName, cancellationToken);
        await _auditRepository.AddAsync(
            new PatchAuditEntry(
                Id: 0, _clock.UtcNow, Environment.UserName, "MAPPING_DELETED",
                ProductId: null, DepotId: null, TargetClients: [],
                PreviewJson: $"{{\"softwareName\":{System.Text.Json.JsonSerializer.Serialize(softwareName)}}}",
                "SUCCESS", ErrorMessage: null),
            cancellationToken);
        return Result.Success(new MappingsResult(await _mappingRepository.ListAsync(cancellationToken)));
    }
}
