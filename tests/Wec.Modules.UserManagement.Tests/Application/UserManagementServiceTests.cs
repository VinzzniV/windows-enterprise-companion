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

    public UserManagementServiceTests()
    {
        _deviceRelationships.GetForDirectorySidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new UserDeviceRelationshipSnapshot(
                new UserDeviceRelationshipCoverage(0, 0, 0, 0, 0),
                []));
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
        Assert.Equal("PC-42", Assert.Single(result.Value.Devices.LinkedDevices).Host);
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

    private UserManagementService CreateService() => new(_directoryUsers, _deviceRelationships);

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
