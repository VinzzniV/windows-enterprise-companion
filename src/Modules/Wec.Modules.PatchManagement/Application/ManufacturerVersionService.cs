using System.Text.Json;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.SoftwareUpdates;
using Wec.Modules.PatchManagement.Persistence;

namespace Wec.Modules.PatchManagement.Application;

public sealed record VersionCheckOutcome(
    string ProductId,
    string? PreviousVersion,
    string? LatestVersion,
    string Status,
    DateTimeOffset CheckedAtUtc,
    string? Error);

public sealed class ManufacturerVersionService
{
    private const string VersionCheckAction = "MANUFACTURER_VERSION_CHECK";
    private readonly IVendorVersionClient _versionClient;
    private readonly IProductVersionSourceRepository _sourceRepository;
    private readonly IPatchAuditRepository _auditRepository;
    private readonly IClock _clock;
    private readonly PatchManagementOptions _options;

    public ManufacturerVersionService(
        IVendorVersionClient versionClient,
        IProductVersionSourceRepository sourceRepository,
        IPatchAuditRepository auditRepository,
        IClock clock,
        IOptions<PatchManagementOptions> options)
    {
        _versionClient = versionClient;
        _sourceRepository = sourceRepository;
        _auditRepository = auditRepository;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<VersionCheckOutcome>> CheckAsync(
        IReadOnlyList<string>? productIds,
        bool force,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductVersionSource> sources = await _sourceRepository.ListAsync(cancellationToken);
        var requested = productIds is { Count: > 0 }
            ? new HashSet<string>(productIds, StringComparer.OrdinalIgnoreCase)
            : null;
        DateTimeOffset dueBefore = _clock.UtcNow - _options.ManufacturerCheckInterval;
        var outcomes = new List<VersionCheckOutcome>();

        foreach (ProductVersionSource source in sources.Where(source =>
            source.Enabled
            && (requested is null || requested.Contains(source.ProductId))
            && (force || source.LastCheckedUtc is null || source.LastCheckedUtc <= dueBefore)))
        {
            DateTimeOffset checkedAt = _clock.UtcNow;
            Result<string> check;
            if (!Uri.TryCreate(source.SourceUrl, UriKind.Absolute, out Uri? sourceUrl))
            {
                check = Result.Failure<string>(new Error(
                    ErrorCode.InvalidRequest,
                    "The configured manufacturer source URL is invalid."));
            }
            else
            {
                check = await _versionClient.GetLatestVersionAsync(
                    new VendorVersionRequest(sourceUrl, source.VersionPattern, _options.ManufacturerRequestTimeout),
                    cancellationToken);
            }

            var outcome = new VersionCheckOutcome(
                source.ProductId,
                source.LatestVersion,
                check.IsSuccess ? check.Value : source.LatestVersion,
                check.IsSuccess ? "SUCCESS" : "FAILED",
                checkedAt,
                check.Error?.Message);
            await _sourceRepository.UpsertAsync(source with
            {
                LatestVersion = outcome.LatestVersion,
                LastCheckedUtc = checkedAt,
                CheckStatus = outcome.Status,
                LastError = outcome.Error,
            }, cancellationToken);
            await _auditRepository.AddAsync(new PatchAuditEntry(
                Id: 0,
                checkedAt,
                Environment.UserName,
                VersionCheckAction,
                source.ProductId,
                DepotId: null,
                TargetClients: [],
                JsonSerializer.Serialize(outcome),
                outcome.Status,
                outcome.Error,
                outcome.PreviousVersion,
                outcome.LatestVersion), cancellationToken);
            outcomes.Add(outcome);
        }

        return outcomes;
    }
}
