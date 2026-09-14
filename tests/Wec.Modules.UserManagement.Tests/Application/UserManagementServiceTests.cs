using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.UserManagement.Application;
using Wec.Modules.UserManagement.Domain;

namespace Wec.Modules.UserManagement.Tests.Application;

public sealed class UserManagementServiceTests
{
    private static readonly Guid ObjectId = new("00112233-4455-6677-8899-aabbccddeeff");
    private readonly IDirectoryUserReadProvider _directoryUsers = Substitute.For<IDirectoryUserReadProvider>();
    private readonly IUserDeviceRelationshipProvider _deviceRelationships = Substitute.For<IUserDeviceRelationshipProvider>();
    private readonly IInstalledSoftwareInventoryProvider _software = Substitute.For<IInstalledSoftwareInventoryProvider>();
    private readonly IDeviceHealthSnapshotProvider _health = Substitute.For<IDeviceHealthSnapshotProvider>();
    private readonly ISecurityReportDataProvider _security = Substitute.For<ISecurityReportDataProvider>();
    private readonly IStoredNessusComputerInventoryProvider _nessus = Substitute.For<IStoredNessusComputerInventoryProvider>();

    public UserManagementServiceTests()
    {
        _deviceRelationships.GetForDirectorySidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new UserDeviceRelationshipSnapshot(
                new UserDeviceRelationshipCoverage(0, 0, 0, 0, 0),
                []));
        _nessus.LoadStoredAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success(new NessusComputerInventory(
                [],
                NessusInventoryAvailability.NotConnected,
                null)));
    }

    [Fact]
    public async Task GetPage_MapsTheDirectoryProjectionWithoutPersistingASecondUserModel()
    {
        DirectoryUserRecord user = User();
        _directoryUsers.GetPageAsync(Arg.Any<DirectoryUserPageQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new DirectoryUserPage(
                true, "corp.example", "DC=corp,DC=example", 2, 25, 51, [user])));
        var query = new DirectoryUserPageQuery(
            CurrentConnection(),
            "alex",
            null,
            "IT",
            DirectoryUserAccountStateFilter.Enabled,
            2,
            25,
            DirectoryUserSortField.DisplayName,
            DirectoryUserSortDirection.Ascending);

        Result<UserPageResult> result = await CreateService()
            .GetPageAsync(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(51, result.Value.TotalCount);
        UserSummary summary = Assert.Single(result.Value.Users);
        Assert.Equal(ObjectId, summary.ObjectId);
        Assert.Equal("Alex Example", summary.DisplayName);
        Assert.Equal("IT", summary.Department);
        Assert.Equal(user.ReplicatedLastLogonAtUtc, summary.ReplicatedLastLogonAtUtc);
        await _directoryUsers.Received(1).GetPageAsync(query, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetProfile_SeparatesIdentityLifecycleAndSidValidatedAccessEvidence()
    {
        _directoryUsers.GetByIdAsync(Arg.Any<DirectoryUserIdentityQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<DirectoryUserRecord?>(User()));
        var relationships = new UserDeviceRelationshipSnapshot(
            new UserDeviceRelationshipCoverage(2, 2, 0, 0, 0),
            [
                new UserLinkedDeviceEvidence(
                    "PC-42",
                    new DateTimeOffset(2026, 8, 27, 8, 0, 0, TimeSpan.Zero),
                    [
                        new UserDeviceRelationshipObservation(
                            UserDeviceRelationshipType.ProfilePresent,
                            "WEC Inventory",
                            new DateTimeOffset(2026, 8, 27, 8, 0, 0, TimeSpan.Zero),
                            UserDeviceRelationshipConfidence.Medium,
                            "Profile evidence.",
                            new DateTimeOffset(2026, 8, 26, 8, 0, 0, TimeSpan.Zero)),
                    ]),
            ]);
        _deviceRelationships.GetForDirectorySidAsync(
                "S-1-5-21-100-200-300-1104",
                Arg.Any<CancellationToken>())
            .Returns(relationships);
        DateTimeOffset capturedAt = new(2026, 8, 27, 7, 30, 0, TimeSpan.Zero);
        _software.GetLatestAsync("PC-42", Arg.Any<CancellationToken>())
            .Returns(new InstalledSoftwareSnapshotData(
                "PC-42",
                capturedAt,
                true,
                [
                    new InstalledSoftwareRecordData("Zulu", "2.0", "Example"),
                    new InstalledSoftwareRecordData("Alpha", "1.0", "Example"),
                ],
                null,
                null));
        _health.GetLatestAsync("PC-42", Arg.Any<CancellationToken>())
            .Returns(new DeviceHealthSnapshotData(
                capturedAt,
                false,
                4,
                3,
                [
                    HealthCheck("Fail", capturedAt),
                    HealthCheck("Warning", capturedAt),
                    HealthCheck("Pass", capturedAt),
                ]));
        _security.GetLatestScanAsync("PC-42", Arg.Any<CancellationToken>())
            .Returns(new SecurityReportData(
                capturedAt,
                "Completed",
                [
                    SecurityFinding("Critical", 4),
                    SecurityFinding("High", 3),
                ],
                new SecurityCoverageReportData(true, true, 2, 2, 2, 0, 0, 0),
                []));
        _nessus.LoadStoredAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success(new NessusComputerInventory(
                [
                    new NessusComputerInventoryItem(
                        "pc-42",
                        "asset-42",
                        "192.0.2.42",
                        capturedAt,
                        1,
                        2,
                        3,
                        4,
                        0,
                        [],
                        ["Weekly"]),
                ],
                NessusInventoryAvailability.Available,
                capturedAt)));

        Result<UserProfileResult> result = await CreateService()
            .GetProfileAsync(
                new DirectoryUserIdentityQuery(CurrentConnection(), ObjectId),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ObjectId, result.Value.Identity.ObjectId);
        Assert.True(result.Value.Lifecycle.Enabled);
        Assert.Equal(DirectoryUserAccessCoverage.Available, result.Value.Access.PrivilegedCoverage);
        Assert.Equal("Domänen-Admins", Assert.Single(result.Value.Access.DirectPrivilegedGroups).Name);
        Assert.Equal(2, result.Value.Access.DirectGroups.Count);
        Assert.Equal(UserDeviceEvidenceCoverage.Available, result.Value.Devices.Coverage);
        Assert.Equal(1, result.Value.Devices.TotalLinkedDeviceCount);
        Assert.False(result.Value.Devices.LinkedDevicesTruncated);
        UserLinkedDeviceProfile device = Assert.Single(result.Value.Devices.LinkedDevices);
        Assert.Equal("PC-42", device.Host);
        Assert.Equal(2, device.Software.InstalledCount);
        Assert.Equal("Alpha", device.Software.Sample[0].Name);
        Assert.Equal(1, device.Health.CriticalCount);
        Assert.Equal(1, device.Health.WarningCount);
        Assert.Equal(1, device.Health.HealthyCount);
        Assert.Equal(1, device.Security.CriticalCount);
        Assert.Equal(1, device.Security.HighCount);
        Assert.True(device.Vulnerabilities.DeviceMatched);
        Assert.Equal(2, device.Vulnerabilities.HighCount);
    }

    [Fact]
    public async Task GetProfile_ReturnsTypedNotFoundWhenTheStableIdentityIsAbsent()
    {
        _directoryUsers.GetByIdAsync(Arg.Any<DirectoryUserIdentityQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<DirectoryUserRecord?>(null));

        Result<UserProfileResult> result = await CreateService()
            .GetProfileAsync(
                new DirectoryUserIdentityQuery(CurrentConnection(), ObjectId),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.Error!.Code);
    }

    [Fact]
    public async Task GetProfile_DoesNotGuessDeviceRelationshipsWithoutADSid()
    {
        _directoryUsers.GetByIdAsync(Arg.Any<DirectoryUserIdentityQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<DirectoryUserRecord?>(User(sid: null)));

        Result<UserProfileResult> result = await CreateService().GetProfileAsync(
            new DirectoryUserIdentityQuery(CurrentConnection(), ObjectId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserDeviceEvidenceCoverage.NotEvaluated, result.Value.Devices.Coverage);
        Assert.Empty(result.Value.Devices.LinkedDevices);
        await _deviceRelationships.DidNotReceiveWithAnyArgs().GetForDirectorySidAsync(default!, default);
        await _software.DidNotReceiveWithAnyArgs().GetLatestAsync(default, default);
        await _health.DidNotReceiveWithAnyArgs().GetLatestAsync(default, default);
        await _security.DidNotReceiveWithAnyArgs().GetLatestScanAsync(default, default);
        await _nessus.DidNotReceiveWithAnyArgs().LoadStoredAsync(default);
    }

    [Fact]
    public async Task GetProfile_ReportsUnavailableInventoryEvidenceAsPartial()
    {
        _directoryUsers.GetByIdAsync(Arg.Any<DirectoryUserIdentityQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<DirectoryUserRecord?>(User()));
        _deviceRelationships.GetForDirectorySidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new UserDeviceRelationshipSnapshot(
                new UserDeviceRelationshipCoverage(1, 0, 0, 1, 0),
                []));

        Result<UserProfileResult> result = await CreateService().GetProfileAsync(
            new DirectoryUserIdentityQuery(CurrentConnection(), ObjectId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserDeviceEvidenceCoverage.Partial, result.Value.Devices.Coverage);
        Assert.Contains("missing", result.Value.Devices.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetProfile_LabelsLegacySnapshotsAsNotCaptured()
    {
        _directoryUsers.GetByIdAsync(Arg.Any<DirectoryUserIdentityQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<DirectoryUserRecord?>(User()));
        _deviceRelationships.GetForDirectorySidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new UserDeviceRelationshipSnapshot(
                new UserDeviceRelationshipCoverage(2, 0, 2, 0, 0),
                []));

        Result<UserProfileResult> result = await CreateService().GetProfileAsync(
            new DirectoryUserIdentityQuery(CurrentConnection(), ObjectId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserDeviceEvidenceCoverage.NotCaptured, result.Value.Devices.Coverage);
        Assert.Contains("predate", result.Value.Devices.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetProfile_BoundsLinkedDevicesAndPrioritizesInteractiveEvidence()
    {
        _directoryUsers.GetByIdAsync(Arg.Any<DirectoryUserIdentityQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<DirectoryUserRecord?>(User()));
        DateTimeOffset newest = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
        List<UserLinkedDeviceEvidence> devices = Enumerable.Range(1, 10)
            .Select(index => new UserLinkedDeviceEvidence(
                $"PC-{index:00}",
                newest.AddHours(-index),
                [ProfileObservation(newest.AddHours(-index))]))
            .ToList();
        devices.Add(new UserLinkedDeviceEvidence(
            "PC-INTERACTIVE",
            newest.AddDays(-30),
            [InteractiveObservation(newest.AddDays(-30))]));
        _deviceRelationships.GetForDirectorySidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new UserDeviceRelationshipSnapshot(
                new UserDeviceRelationshipCoverage(11, 11, 0, 0, 0),
                devices));

        Result<UserProfileResult> result = await CreateService(maxLinkedDevices: 8).GetProfileAsync(
            new DirectoryUserIdentityQuery(CurrentConnection(), ObjectId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(11, result.Value.Devices.TotalLinkedDeviceCount);
        Assert.True(result.Value.Devices.LinkedDevicesTruncated);
        Assert.Equal(8, result.Value.Devices.LinkedDevices.Count);
        Assert.Equal("PC-INTERACTIVE", result.Value.Devices.LinkedDevices[0].Host);
        Assert.Equal("PC-01", result.Value.Devices.LinkedDevices[1].Host);
        await _software.Received(8).GetLatestAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _nessus.Received(1).LoadStoredAsync(Arg.Any<CancellationToken>());
    }

    private UserManagementService CreateService(int maxLinkedDevices = 8) => new(
        _directoryUsers,
        _deviceRelationships,
        _software,
        _health,
        _security,
        _nessus,
        Options.Create(new UserManagementOptions
        {
            MaxLinkedDevices = maxLinkedDevices,
            SoftwareSampleLimit = 5,
        }));

    private static DeviceHealthCheckData HealthCheck(string status, DateTimeOffset capturedAt) => new(
        $"health-{status}",
        status,
        status,
        "System",
        "PC-42",
        capturedAt);

    private static SecurityFindingReportData SecurityFinding(string severity, int severityRank) => new(
        $"security-{severity}",
        severity,
        "Description",
        severity,
        severityRank,
        "System",
        "PC-42",
        "Review",
        null);

    private static UserDeviceRelationshipObservation ProfileObservation(DateTimeOffset observedAt) => new(
        UserDeviceRelationshipType.ProfilePresent,
        "WEC Inventory",
        observedAt,
        UserDeviceRelationshipConfidence.Medium,
        "Profile evidence.",
        observedAt);

    private static UserDeviceRelationshipObservation InteractiveObservation(DateTimeOffset observedAt) => new(
        UserDeviceRelationshipType.LastInteractiveUser,
        "WEC Inventory",
        observedAt,
        UserDeviceRelationshipConfidence.High,
        "Interactive evidence.",
        null);

    private static DirectoryUserRecord User(string? sid = "S-1-5-21-100-200-300-1104")
    {
        var privileged = new DirectoryUserGroup(
            "CN=Domänen-Admins,CN=Users,DC=corp,DC=example",
            "Domänen-Admins");
        return new DirectoryUserRecord(
            ObjectId,
            sid,
            "Alex Example",
            "a.example",
            "a.example@corp.example",
            "alex@example.test",
            "E-1042",
            "IT",
            "Administrator",
            "CN=Manager,OU=Users,DC=corp,DC=example",
            "CN=Alex Example,OU=Users,DC=corp,DC=example",
            "OU=Users,DC=corp,DC=example",
            true,
            new DateTimeOffset(2024, 1, 2, 12, 0, 0, TimeSpan.Zero),
            null,
            new DateTimeOffset(2026, 8, 25, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero),
            false,
            [privileged, new DirectoryUserGroup("CN=GG-App,OU=Groups,DC=corp,DC=example", "GG-App")],
            new DirectoryUserPrivilegedAccess(
                DirectoryUserAccessCoverage.Available,
                "SID allowlist evaluated.",
                [privileged]));
    }

    private static DirectoryUserReadConnection CurrentConnection() =>
        new(null, null, ScanCredentials.CurrentUser);
}
