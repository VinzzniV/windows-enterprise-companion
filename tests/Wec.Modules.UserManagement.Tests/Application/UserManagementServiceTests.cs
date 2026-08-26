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

        Result<UserPageResult> result = await new UserManagementService(_directoryUsers)
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

        Result<UserProfileResult> result = await new UserManagementService(_directoryUsers)
            .GetProfileAsync(
                new DirectoryUserIdentityQuery(CurrentConnection(), ObjectId),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ObjectId, result.Value.Identity.ObjectId);
        Assert.True(result.Value.Lifecycle.Enabled);
        Assert.Equal(DirectoryUserAccessCoverage.Available, result.Value.Access.PrivilegedCoverage);
        Assert.Equal("Domänen-Admins", Assert.Single(result.Value.Access.DirectPrivilegedGroups).Name);
        Assert.Equal(2, result.Value.Access.DirectGroups.Count);
    }

    [Fact]
    public async Task GetProfile_ReturnsTypedNotFoundWhenTheStableIdentityIsAbsent()
    {
        _directoryUsers.GetByIdAsync(Arg.Any<DirectoryUserIdentityQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<DirectoryUserRecord?>(null));

        Result<UserProfileResult> result = await new UserManagementService(_directoryUsers)
            .GetProfileAsync(
                new DirectoryUserIdentityQuery(CurrentConnection(), ObjectId),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.Error!.Code);
    }

    private static DirectoryUserRecord User()
    {
        var privileged = new DirectoryUserGroup(
            "CN=Domänen-Admins,CN=Users,DC=corp,DC=example",
            "Domänen-Admins");
        return new DirectoryUserRecord(
            ObjectId,
            "S-1-5-21-100-200-300-1104",
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
