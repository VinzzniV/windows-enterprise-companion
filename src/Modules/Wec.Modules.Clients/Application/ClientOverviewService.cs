using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Targets;

namespace Wec.Modules.Clients.Application;

public enum ClientOverviewFreshness
{
    Missing = 0,
    Fresh = 1,
    Stale = 2,
    Unknown = 3,
}

public sealed record ClientOverviewSourceMetadata(
    string Source,
    string Provenance,
    ClientOverviewFreshness Freshness,
    DateTimeOffset? CapturedAtUtc,
    long? AgeSeconds,
    bool IsComplete,
    string Coverage,
    string DetailSection);

public sealed record ClientOverviewDisk(
    string Model,
    long SizeBytes,
    string? InterfaceType);

public sealed record ClientInventoryOverview(
    ClientOverviewSourceMetadata Metadata,
    string CpuName,
    int PhysicalCores,
    int LogicalProcessors,
    long TotalMemoryBytes,
    string OperatingSystem,
    string OperatingSystemVersion,
    string OperatingSystemBuild,
    string? Architecture,
    IReadOnlyList<ClientOverviewDisk> Disks);

public sealed record ClientSoftwareOverviewItem(
    string Name,
    string? Version,
    string? Publisher);

public sealed record ClientSoftwareOverview(
    ClientOverviewSourceMetadata Metadata,
    int InstalledCount,
    IReadOnlyList<ClientSoftwareOverviewItem> Sample);

public sealed record ClientHealthIssue(
    string DiagnosticId,
    string Title,
    string Status,
    string AffectedResource);

public sealed record ClientHealthOverview(
    ClientOverviewSourceMetadata Metadata,
    int CriticalCount,
    int WarningCount,
    int UnknownCount,
    int HealthyCount,
    IReadOnlyList<ClientHealthIssue> Issues);

public sealed record ClientSecurityFinding(
    string FindingId,
    string Title,
    string Severity,
    string AffectedResource);

public sealed record ClientSecurityOverview(
    ClientOverviewSourceMetadata Metadata,
    string? ScanStatus,
    int CriticalCount,
    int HighCount,
    int MediumCount,
    int LowCount,
    IReadOnlyList<ClientSecurityFinding> TopFindings);

public sealed record ClientUserOverview(
    ClientOverviewSourceMetadata Metadata,
    int UnresolvedProfileCount,
    IReadOnlyList<ClientObservedUserEvidence> Observations);

public sealed record ClientOverviewResult(
    string Host,
    ClientInventoryOverview? Inventory,
    ClientSoftwareOverview? Software,
    ClientHealthOverview? Health,
    ClientSecurityOverview? Security,
    ClientUserOverview? Users,
    IReadOnlyList<ClientOverviewSourceMetadata> Sources);

internal sealed class ClientOverviewService
{
    private const int SoftwareSampleLimit = 10;
    private const int SecurityFindingLimit = 5;

    private readonly IInventoryReportDataProvider _inventory;
    private readonly IInstalledSoftwareInventoryProvider _software;
    private readonly IDeviceHealthSnapshotProvider _health;
    private readonly ISecurityReportDataProvider _security;
    private readonly IClientUserRelationshipProvider _clientUsers;
    private readonly IClock _clock;
    private readonly ClientOverviewOptions _options;

    public ClientOverviewService(
        IInventoryReportDataProvider inventory,
        IInstalledSoftwareInventoryProvider software,
        IDeviceHealthSnapshotProvider health,
        ISecurityReportDataProvider security,
        IClientUserRelationshipProvider clientUsers,
        IClock clock,
        IOptions<ClientOverviewOptions> options)
    {
        _inventory = inventory;
        _software = software;
        _health = health;
        _security = security;
        _clientUsers = clientUsers;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<ClientOverviewResult> GetAsync(string host, CancellationToken cancellationToken)
    {
        string? providerHost = IsLocalHost(host) ? null : host;
        InventoryReportData? inventory = await _inventory.GetLatestAsync(providerHost, cancellationToken);
        InstalledSoftwareSnapshotData? software = await _software.GetLatestAsync(providerHost, cancellationToken);
        DeviceHealthSnapshotData? health = await _health.GetLatestAsync(providerHost, cancellationToken);
        SecurityReportData? security = await _security.GetLatestScanAsync(providerHost, cancellationToken);
        ClientUserRelationshipSnapshot? users = await _clientUsers.GetLatestAsync(providerHost, cancellationToken);

        ClientOverviewSourceMetadata inventoryMetadata = Metadata(
            "Inventory",
            "Persisted WMI/CIM hardware snapshot",
            inventory?.CapturedAtUtc,
            inventory is not null,
            inventory is null ? "No stored hardware snapshot." : "Hardware and operating-system snapshot available.",
            "inventory",
            _options.MaximumInventoryAge);
        ClientOverviewSourceMetadata softwareMetadata = Metadata(
            "Installed software",
            "Persisted Inventory software capture",
            software?.CapturedAtUtc,
            software?.IsComplete == true,
            SoftwareCoverage(software),
            "inventory",
            _options.MaximumSoftwareAge);
        ClientOverviewSourceMetadata healthMetadata = Metadata(
            "Health",
            "Latest persisted on-demand Health run",
            health?.CompletedAtUtc,
            health?.IsComplete == true,
            health is null
                ? "No stored Health run."
                : $"{health.ObservedCheckCount} of {health.ExpectedCheckCount} expected checks observed.",
            "diagnostics",
            _options.MaximumHealthAge);
        ClientOverviewSourceMetadata securityMetadata = Metadata(
            "Security",
            "Latest persisted Security scan and per-check coverage",
            security?.CompletedAtUtc,
            SecurityComplete(security),
            SecurityCoverage(security),
            "security",
            _options.MaximumSecurityAge);
        ClientOverviewSourceMetadata usersMetadata = Metadata(
            "Linked users",
            "Latest stored WEC Inventory user evidence",
            users?.InventoryCapturedAtUtc,
            users?.Availability == ClientUserEvidenceAvailability.Available,
            users?.CoverageExplanation ?? "No stored user/device relationship evidence.",
            "inventory",
            _options.MaximumInventoryAge);

        return new ClientOverviewResult(
            host,
            inventory is null ? null : InventoryOverview(inventoryMetadata, inventory),
            software is null ? null : SoftwareOverview(softwareMetadata, software),
            health is null ? null : HealthOverview(healthMetadata, health),
            security is null ? null : SecurityOverview(securityMetadata, security),
            users is null ? null : new ClientUserOverview(
                usersMetadata,
                users.UnresolvedProfileCount,
                users.Observations),
            [inventoryMetadata, softwareMetadata, healthMetadata, securityMetadata, usersMetadata]);
    }

    private ClientOverviewSourceMetadata Metadata(
        string source,
        string provenance,
        DateTimeOffset? capturedAtUtc,
        bool isComplete,
        string coverage,
        string detailSection,
        TimeSpan maximumAge)
    {
        if (capturedAtUtc is null)
        {
            return new ClientOverviewSourceMetadata(
                source, provenance, ClientOverviewFreshness.Missing, null, null,
                IsComplete: false, coverage, detailSection);
        }

        TimeSpan age = _clock.UtcNow - capturedAtUtc.Value;
        if (age < TimeSpan.Zero)
        {
            return new ClientOverviewSourceMetadata(
                source, provenance, ClientOverviewFreshness.Unknown, capturedAtUtc, null,
                isComplete, "Source timestamp is in the future; age cannot be trusted.", detailSection);
        }

        return new ClientOverviewSourceMetadata(
            source,
            provenance,
            age > maximumAge ? ClientOverviewFreshness.Stale : ClientOverviewFreshness.Fresh,
            capturedAtUtc,
            checked((long)age.TotalSeconds),
            isComplete,
            coverage,
            detailSection);
    }

    private static ClientInventoryOverview InventoryOverview(
        ClientOverviewSourceMetadata metadata,
        InventoryReportData inventory) => new(
        metadata,
        inventory.Cpu.Name,
        inventory.Cpu.PhysicalCores,
        inventory.Cpu.LogicalProcessors,
        inventory.MemoryBanks.Sum(bank => bank.CapacityBytes),
        inventory.OperatingSystem.Caption,
        inventory.OperatingSystem.Version,
        inventory.OperatingSystem.BuildNumber,
        inventory.OperatingSystem.Architecture,
        [.. inventory.Disks.Select(disk => new ClientOverviewDisk(
            disk.Model, disk.SizeBytes, disk.InterfaceType))]);

    private static ClientSoftwareOverview SoftwareOverview(
        ClientOverviewSourceMetadata metadata,
        InstalledSoftwareSnapshotData software) => new(
        metadata,
        software.Software.Count,
        [.. software.Software
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Take(SoftwareSampleLimit)
            .Select(entry => new ClientSoftwareOverviewItem(entry.Name, entry.Version, entry.Publisher))]);

    private static ClientHealthOverview HealthOverview(
        ClientOverviewSourceMetadata metadata,
        DeviceHealthSnapshotData health) => new(
        metadata,
        health.Checks.Count(check => string.Equals(check.Status, "Fail", StringComparison.Ordinal)),
        health.Checks.Count(check => string.Equals(check.Status, "Warning", StringComparison.Ordinal)),
        health.Checks.Count(check => string.Equals(check.Status, "NotRun", StringComparison.Ordinal)),
        health.Checks.Count(check => string.Equals(check.Status, "Pass", StringComparison.Ordinal)),
        [.. health.Checks
            .Where(check => !string.Equals(check.Status, "Pass", StringComparison.Ordinal))
            .Select(check => new ClientHealthIssue(
                check.DiagnosticId, check.Title, check.Status, check.AffectedResource))]);

    private static ClientSecurityOverview SecurityOverview(
        ClientOverviewSourceMetadata metadata,
        SecurityReportData security) => new(
        metadata,
        security.Status,
        CountSeverity(security, "Critical"),
        CountSeverity(security, "High"),
        CountSeverity(security, "Medium"),
        CountSeverity(security, "Low"),
        [.. security.Findings
            .OrderByDescending(finding => finding.SeverityRank)
            .ThenBy(finding => finding.Title, StringComparer.OrdinalIgnoreCase)
            .Take(SecurityFindingLimit)
            .Select(finding => new ClientSecurityFinding(
                finding.FindingId, finding.Title, finding.Severity, finding.AffectedResource))]);

    private static int CountSeverity(SecurityReportData security, string severity) =>
        security.Findings.Count(finding => string.Equals(finding.Severity, severity, StringComparison.Ordinal));

    private static bool SecurityComplete(SecurityReportData? security) =>
        security is not null
        && string.Equals(security.Status, "Completed", StringComparison.Ordinal)
        && security.Coverage.IsKnown
        && security.Coverage.IsComplete;

    private static string SecurityCoverage(SecurityReportData? security)
    {
        if (security is null)
        {
            return "No stored Security scan.";
        }

        return security.Coverage.IsKnown
            ? $"{security.Coverage.SucceededChecks} of {security.Coverage.ApplicableChecks} applicable checks succeeded."
            : "Per-check coverage is unavailable for this legacy scan.";
    }

    private static string SoftwareCoverage(InstalledSoftwareSnapshotData? software)
    {
        if (software is null)
        {
            return "No stored software capture.";
        }

        if (software.IsComplete)
        {
            return $"Complete capture with {software.Software.Count} installed entries.";
        }

        return software.ErrorCode is null
            ? "Software coverage is unavailable for this legacy snapshot."
            : $"Software capture failed ({software.ErrorCode}).";
    }

    private static bool IsLocalHost(string host)
    {
        return HostAddress.IsExactLocalName(host, Environment.MachineName);
    }
}
