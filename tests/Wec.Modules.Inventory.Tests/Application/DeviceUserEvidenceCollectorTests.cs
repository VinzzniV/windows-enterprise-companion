using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Inventory.Application;
using Wec.Modules.Inventory.Domain;

namespace Wec.Modules.Inventory.Tests.Application;

public sealed class DeviceUserEvidenceCollectorTests
{
    private readonly IWmiQueryService _wmi = Substitute.For<IWmiQueryService>();

    [Fact]
    public async Task CollectsExactDomainUserAndBoundedFilteredProfiles()
    {
        SetUp("Win32_ComputerSystem", [Instance(("UserName", @"CORP\alex"))]);
        SetUp("Win32_UserAccount", [Instance(
            ("SID", "S-1-5-21-1-2-3-1104"),
            ("Domain", "CORP"),
            ("Name", "alex"),
            ("LocalAccount", false))]);
        SetUp("Win32_UserProfile",
        [
            Profile("S-1-5-21-1-2-3-1104", special: false, new DateTimeOffset(2026, 8, 27, 8, 0, 0, TimeSpan.Zero)),
            Profile("S-1-5-21-1-2-3-1200", special: false, new DateTimeOffset(2026, 8, 26, 8, 0, 0, TimeSpan.Zero)),
            Profile("S-1-12-1-10-20-30-40", special: false, null),
            Profile("S-1-5-21-1-2-3-500", special: false, null),
            Profile("S-1-5-80-1-2-3-4-5", special: false, null),
            Profile("S-1-5-21-1-2-3-1300", special: true, null),
        ]);
        DeviceUserEvidenceCollector collector = CreateCollector(maxProfiles: 2);

        DeviceUserEvidence evidence = await collector.CollectAsync(
            ScanTarget.Remote("PC-42"),
            ScanCredentials.CurrentUser,
            CancellationToken.None);

        Assert.Equal(UserEvidenceSourceState.Available, evidence.InteractiveUserState);
        Assert.Equal("S-1-5-21-1-2-3-1104", evidence.InteractiveUser!.Sid);
        Assert.Equal("CORP", evidence.InteractiveUser.Domain);
        Assert.Equal("alex", evidence.InteractiveUser.AccountName);
        Assert.Equal(UserEvidenceSourceState.Available, evidence.LocalProfilesState);
        Assert.Equal(2, evidence.LocalProfiles!.Count);
        Assert.True(evidence.LocalProfilesTruncated);
        Assert.DoesNotContain(evidence.LocalProfiles, profile => profile.Sid.EndsWith("-500", StringComparison.Ordinal));
        await _wmi.Received().QueryAsync(
            Arg.Any<ScanTarget>(),
            Arg.Any<ScanCredentials>(),
            Arg.Any<ConnectionOptions>(),
            @"root\cimv2",
            Arg.Is<string>(query => query == "SELECT SID, Special, LastUseTime FROM Win32_UserProfile"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExcludesLocalInteractiveAccountFromPersistedEvidence()
    {
        SetUp("Win32_ComputerSystem", [Instance(("UserName", @"PC-42\localadmin"))]);
        SetUp("Win32_UserAccount", [Instance(
            ("SID", "S-1-5-21-1-2-3-1104"),
            ("Domain", "PC-42"),
            ("Name", "localadmin"),
            ("LocalAccount", true))]);
        SetUp("Win32_UserProfile", []);

        DeviceUserEvidence evidence = await CreateCollector().CollectAsync(
            ScanTarget.Remote("PC-42"),
            ScanCredentials.CurrentUser,
            CancellationToken.None);

        Assert.Equal(UserEvidenceSourceState.Available, evidence.InteractiveUserState);
        Assert.Null(evidence.InteractiveUser);
    }

    [Fact]
    public async Task SourceFailuresRemainExplicitWithoutPersistingRawProviderMessages()
    {
        _wmi.QueryAsync(
                Arg.Any<ScanTarget>(),
                Arg.Any<ScanCredentials>(),
                Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<WmiInstance>>(
                Error.WmiUnavailable("provider detail with a sensitive account name")));

        DeviceUserEvidence evidence = await CreateCollector().CollectAsync(
            ScanTarget.Local,
            ScanCredentials.CurrentUser,
            CancellationToken.None);

        Assert.Equal(UserEvidenceSourceState.Unavailable, evidence.InteractiveUserState);
        Assert.Equal(UserEvidenceSourceState.Unavailable, evidence.LocalProfilesState);
        Assert.Equal("WMI_UNAVAILABLE", evidence.InteractiveUserError!.Code);
        Assert.DoesNotContain("sensitive", evidence.InteractiveUserError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(evidence.LocalProfiles);
        Assert.False(evidence.LocalProfilesTruncated);
    }

    [Theory]
    [InlineData("S-1-5-21-1-2-3-1104", true)]
    [InlineData("S-1-12-1-10-20-30-40", true)]
    [InlineData("S-1-5-18", false)]
    [InlineData("S-1-5-80-1-2-3-4-5", false)]
    [InlineData("S-1-5-82-1-2-3-4-5", false)]
    [InlineData("S-1-5-21-1-2-3-500", false)]
    [InlineData("not-a-sid", false)]
    public void SidFilterKeepsOnlySupportedNonBuiltInUserSids(string sid, bool expected)
    {
        Assert.Equal(expected, DeviceUserEvidenceCollector.IsEligibleUserSid(sid));
    }

    [Fact]
    public void TimestampMappingNormalizesKnownValuesAndRejectsUnknownShapes()
    {
        DateTimeOffset source = new(2026, 8, 27, 10, 0, 0, TimeSpan.FromHours(2));
        Assert.Equal(new DateTimeOffset(2026, 8, 27, 8, 0, 0, TimeSpan.Zero),
            DeviceUserEvidenceCollector.ToUtcTimestamp(source));
        Assert.Null(DeviceUserEvidenceCollector.ToUtcTimestamp("20260827080000.000000+000"));
    }

    private DeviceUserEvidenceCollector CreateCollector(int maxProfiles = 100) => new(
        _wmi,
        Options.Create(new InventoryOptions { MaxUserProfiles = maxProfiles }),
        Options.Create(new RemoteScanOptions()));

    private void SetUp(string className, IReadOnlyList<WmiInstance> instances) =>
        _wmi.QueryAsync(
                Arg.Any<ScanTarget>(),
                Arg.Any<ScanCredentials>(),
                Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Is<string>(query => query.Contains(className, StringComparison.Ordinal)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success(instances));

    private static WmiInstance Profile(string sid, bool special, DateTimeOffset? lastUse) =>
        Instance(("SID", sid), ("Special", special), ("LastUseTime", lastUse));

    private static WmiInstance Instance(params (string Name, object? Value)[] properties) =>
        new(properties.ToDictionary(property => property.Name, property => property.Value));
}
