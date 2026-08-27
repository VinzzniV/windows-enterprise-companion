using Microsoft.Extensions.Options;
using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Modules.UserManagement.Domain;

namespace Wec.Modules.UserManagement.Application;

internal sealed class UserManagementService
{
    private readonly IDirectoryUserReadProvider _directoryUsers;
    private readonly IUserDeviceRelationshipProvider _deviceRelationships;
    private readonly IInstalledSoftwareInventoryProvider _software;
    private readonly IDeviceHealthSnapshotProvider _health;
    private readonly ISecurityReportDataProvider _security;
    private readonly IStoredNessusComputerInventoryProvider _nessus;
    private readonly UserManagementOptions _options;

    public UserManagementService(
        IDirectoryUserReadProvider directoryUsers,
        IUserDeviceRelationshipProvider deviceRelationships,
        IInstalledSoftwareInventoryProvider software,
        IDeviceHealthSnapshotProvider health,
        ISecurityReportDataProvider security,
        IStoredNessusComputerInventoryProvider nessus,
        IOptions<UserManagementOptions> options)
    {
        _directoryUsers = directoryUsers;
        _deviceRelationships = deviceRelationships;
        _software = software;
        _health = health;
        _security = security;
        _nessus = nessus;
        _options = options.Value;
    }

    public async Task<Result<UserPageResult>> GetPageAsync(
        DirectoryUserPageQuery query,
        CancellationToken cancellationToken)
    {
        Result<DirectoryUserPage> page = await _directoryUsers.GetPageAsync(query, cancellationToken);
        return page.IsFailure
            ? Result.Failure<UserPageResult>(page.Error!)
            : Result.Success(new UserPageResult(
                page.Value.DomainJoined,
                page.Value.DomainName,
                page.Value.BaseDistinguishedName,
                page.Value.Page,
                page.Value.PageSize,
                page.Value.TotalCount,
                [.. page.Value.Users.Select(ToSummary)]));
    }

    public async Task<Result<UserProfileResult>> GetProfileAsync(
        DirectoryUserIdentityQuery query,
        CancellationToken cancellationToken)
    {
        Result<DirectoryUserRecord?> result = await _directoryUsers.GetByIdAsync(query, cancellationToken);
        if (result.IsFailure)
        {
            return Result.Failure<UserProfileResult>(result.Error!);
        }

        if (result.Value is null)
        {
            return Result.Failure<UserProfileResult>(new Error(
                ErrorCode.NotFound,
                "The requested directory user was not found."));
        }

        DirectoryUserRecord user = result.Value;
        UserDeviceProfile devices = await GetDeviceProfileAsync(user.Sid, cancellationToken);
        return Result.Success(new UserProfileResult(
            new UserIdentityProfile(
                user.ObjectId,
                user.Sid,
                user.DisplayName,
                user.SamAccountName,
                user.UserPrincipalName,
                user.Mail,
                user.EmployeeId,
                user.Department,
                user.Title,
                user.ManagerDistinguishedName,
                user.DistinguishedName,
                user.OrganizationalUnitPath),
            new UserLifecycleProfile(
                user.Enabled,
                user.CreatedAtUtc,
                user.AccountExpiresAtUtc,
                user.ReplicatedLastLogonAtUtc,
                user.PasswordLastSetAtUtc,
                user.PasswordExpiresAtUtc,
                user.PasswordNeverExpires),
            new UserAccessProfile(
                user.DirectGroups,
                user.PrivilegedAccess.Coverage,
                user.PrivilegedAccess.Explanation,
                user.PrivilegedAccess.DirectMemberships),
            devices));
    }

    private async Task<UserDeviceProfile> GetDeviceProfileAsync(
        string? directorySid,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(directorySid))
        {
            return new UserDeviceProfile(
                UserDeviceEvidenceCoverage.NotEvaluated,
                "The directory user has no SID, so device evidence cannot be matched safely.",
                EmptyDeviceCoverage(),
                TotalLinkedDeviceCount: 0,
                LinkedDevicesTruncated: false,
                []);
        }

        UserDeviceRelationshipSnapshot snapshot = await _deviceRelationships.GetForDirectorySidAsync(
            directorySid,
            cancellationToken);
        UserDeviceEvidenceCoverage coverage = ToCoverage(snapshot.Coverage);
        List<UserLinkedDeviceEvidence> selectedDevices = snapshot.Devices
            .OrderByDescending(HasInteractiveEvidence)
            .ThenByDescending(device => device.InventoryCapturedAtUtc)
            .ThenBy(device => device.Host, StringComparer.OrdinalIgnoreCase)
            .Take(_options.MaxLinkedDevices)
            .ToList();
        Result<NessusComputerInventory>? nessusResult = selectedDevices.Count == 0
            ? null
            : await _nessus.LoadStoredAsync(cancellationToken);
        var deviceProfiles = new List<UserLinkedDeviceProfile>(selectedDevices.Count);
        foreach (UserLinkedDeviceEvidence device in selectedDevices)
        {
            deviceProfiles.Add(await ComposeDeviceAsync(device, nessusResult, cancellationToken));
        }

        return new UserDeviceProfile(
            coverage,
            CoverageExplanation(coverage, snapshot.Coverage),
            snapshot.Coverage,
            snapshot.Devices.Count,
            snapshot.Devices.Count > selectedDevices.Count,
            deviceProfiles);
    }

    private async Task<UserLinkedDeviceProfile> ComposeDeviceAsync(
        UserLinkedDeviceEvidence device,
        Result<NessusComputerInventory>? nessusResult,
        CancellationToken cancellationToken)
    {
        string? providerHost = IsLocalHost(device.Host) ? null : device.Host;
        InstalledSoftwareSnapshotData? software = await _software.GetLatestAsync(providerHost, cancellationToken);
        DeviceHealthSnapshotData? health = await _health.GetLatestAsync(providerHost, cancellationToken);
        SecurityReportData? security = await _security.GetLatestScanAsync(providerHost, cancellationToken);
        return new UserLinkedDeviceProfile(
            device.Host,
            device.InventoryCapturedAtUtc,
            device.Observations,
            ToSoftwareProfile(software),
            ToHealthProfile(health),
            ToSecurityProfile(security),
            ToVulnerabilityProfile(device.Host, nessusResult));
    }

    private UserDeviceSoftwareProfile ToSoftwareProfile(InstalledSoftwareSnapshotData? software)
    {
        if (software is null)
        {
            return new UserDeviceSoftwareProfile(
                false, false, null, 0, [], "No stored installed-software capture.");
        }

        string explanation = software.IsComplete
            ? $"Complete stored capture with {software.Software.Count} installed entries."
            : software.ErrorCode is null
                ? "Installed-software coverage is unavailable for this legacy snapshot."
                : $"Installed-software capture failed ({software.ErrorCode}).";
        return new UserDeviceSoftwareProfile(
            true,
            software.IsComplete,
            software.CapturedAtUtc,
            software.Software.Count,
            [.. software.Software
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Take(_options.SoftwareSampleLimit)
                .Select(entry => new UserDeviceSoftwareItem(entry.Name, entry.Version, entry.Publisher))],
            explanation);
    }

    private static UserDeviceHealthProfile ToHealthProfile(DeviceHealthSnapshotData? health)
    {
        if (health is null)
        {
            return new UserDeviceHealthProfile(
                false, false, null, 0, 0, 0, 0, "No stored Health run.");
        }

        return new UserDeviceHealthProfile(
            true,
            health.IsComplete,
            health.CompletedAtUtc,
            CountHealth(health, "Fail"),
            CountHealth(health, "Warning"),
            CountHealth(health, "NotRun"),
            CountHealth(health, "Pass"),
            $"{health.ObservedCheckCount} of {health.ExpectedCheckCount} expected checks observed.");
    }

    private static UserDeviceSecurityProfile ToSecurityProfile(SecurityReportData? security)
    {
        if (security is null)
        {
            return new UserDeviceSecurityProfile(
                false, false, null, null, 0, 0, 0, 0, "No stored Security scan.");
        }

        bool isComplete = string.Equals(security.Status, "Completed", StringComparison.Ordinal)
            && security.Coverage.IsKnown
            && security.Coverage.IsComplete;
        string explanation = security.Coverage.IsKnown
            ? $"{security.Coverage.SucceededChecks} of {security.Coverage.ApplicableChecks} applicable checks succeeded."
            : "Per-check coverage is unavailable for this legacy scan.";
        return new UserDeviceSecurityProfile(
            true,
            isComplete,
            security.CompletedAtUtc,
            security.Status,
            CountSecurity(security, "Critical"),
            CountSecurity(security, "High"),
            CountSecurity(security, "Medium"),
            CountSecurity(security, "Low"),
            explanation);
    }

    private static UserDeviceVulnerabilityProfile ToVulnerabilityProfile(
        string host,
        Result<NessusComputerInventory>? nessusResult)
    {
        if (nessusResult is null || nessusResult.IsFailure)
        {
            return new UserDeviceVulnerabilityProfile(
                NessusInventoryAvailability.Unavailable,
                false,
                null,
                0,
                0,
                0,
                0,
                "Stored Nessus inventory is unavailable.");
        }

        NessusComputerInventory inventory = nessusResult.Value;
        NessusComputerInventoryItem? item = inventory.Computers
            .Where(candidate => HostEquals(candidate.ComputerName, host))
            .OrderByDescending(candidate => candidate.LastCompletedScanUtc)
            .FirstOrDefault();
        if (item is null)
        {
            return new UserDeviceVulnerabilityProfile(
                inventory.Availability,
                false,
                inventory.LastSuccessfulSyncUtc,
                0,
                0,
                0,
                0,
                "No stored Nessus asset matched this device.");
        }

        return new UserDeviceVulnerabilityProfile(
            inventory.Availability,
            true,
            item.LastCompletedScanUtc ?? inventory.LastSuccessfulSyncUtc,
            item.Critical,
            item.High,
            item.Medium,
            item.Low,
            "Counts come from the latest stored Nessus inventory for this device.");
    }

    private static int CountHealth(DeviceHealthSnapshotData health, string status) =>
        health.Checks.Count(check => string.Equals(check.Status, status, StringComparison.Ordinal));

    private static int CountSecurity(SecurityReportData security, string severity) =>
        security.Findings.Count(finding => string.Equals(finding.Severity, severity, StringComparison.Ordinal));

    private static bool HasInteractiveEvidence(UserLinkedDeviceEvidence device) =>
        device.Observations.Any(observation =>
            observation.RelationshipType == UserDeviceRelationshipType.LastInteractiveUser);

    private static bool IsLocalHost(string host) => HostEquals(host, Environment.MachineName);

    private static bool HostEquals(string left, string right) =>
        string.Equals(ShortHost(left), ShortHost(right), StringComparison.OrdinalIgnoreCase);

    private static string ShortHost(string host) => host.Trim().Split('.')[0];

    private static UserDeviceEvidenceCoverage ToCoverage(UserDeviceRelationshipCoverage coverage)
    {
        if (coverage.StoredDeviceCount > 0
            && coverage.NotCapturedDeviceCount == coverage.StoredDeviceCount
            && coverage.UnavailableDeviceCount == 0)
        {
            return UserDeviceEvidenceCoverage.NotCaptured;
        }

        if (coverage.NotCapturedDeviceCount > 0
            || coverage.UnavailableDeviceCount > 0
            || coverage.TruncatedDeviceCount > 0)
        {
            return UserDeviceEvidenceCoverage.Partial;
        }

        return UserDeviceEvidenceCoverage.Available;
    }

    private static string CoverageExplanation(
        UserDeviceEvidenceCoverage coverage,
        UserDeviceRelationshipCoverage sourceCoverage) => coverage switch
    {
        UserDeviceEvidenceCoverage.Available when sourceCoverage.StoredDeviceCount == 0 =>
            "No stored Inventory devices are available for relationship evaluation.",
        UserDeviceEvidenceCoverage.Available =>
            "All stored Inventory devices contain evaluated user relationship evidence.",
        UserDeviceEvidenceCoverage.NotCaptured =>
            "Stored Inventory snapshots predate user relationship evidence; run an explicit Inventory scan to evaluate them.",
        _ =>
            "Some stored devices have missing, unavailable or truncated Inventory user evidence.",
    };

    private static UserDeviceRelationshipCoverage EmptyDeviceCoverage() => new(0, 0, 0, 0, 0);

    private static UserSummary ToSummary(DirectoryUserRecord user) => new(
        user.ObjectId,
        user.DisplayName,
        user.SamAccountName,
        user.UserPrincipalName,
        user.EmployeeId,
        user.Department,
        user.Title,
        user.OrganizationalUnitPath,
        user.Enabled,
        user.ReplicatedLastLogonAtUtc);
}
