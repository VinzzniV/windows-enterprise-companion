using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Privileges;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Security.Application.Checks;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Tests.Application;

public class FirewallProfilesCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 2, 15, 0, 0, TimeSpan.Zero);

    private readonly IWmiQueryService _wmiQueryService = Substitute.For<IWmiQueryService>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private FirewallProfilesCheck CreateCheck()
    {
        _clock.UtcNow.Returns(Now);
        return new FirewallProfilesCheck(_wmiQueryService, _clock, NullLogger<FirewallProfilesCheck>.Instance);
    }

    private void SetUpProfiles(params WmiInstance[] profiles) =>
        _wmiQueryService
            .QueryAsync(
                Arg.Any<ScanTarget>(),
                Arg.Any<ScanCredentials>(),
                Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<WmiInstance>>(profiles));

    private static WmiInstance Profile(string name, object? enabled) =>
        new(new Dictionary<string, object?> { ["Name"] = name, ["Enabled"] = enabled });

    [Fact]
    public async Task AllProfilesEnabled_ProducesNoFindings()
    {
        SetUpProfiles(Profile("Domain", true), Profile("Private", true), Profile("Public", true));

        IReadOnlyList<SecurityFinding> findings = await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None);

        Assert.Empty(findings);
    }

    [Fact]
    public async Task DisabledProfile_ProducesHighSeverityFirewallFinding()
    {
        SetUpProfiles(Profile("Domain", true), Profile("Public", false));

        IReadOnlyList<SecurityFinding> findings = await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None);

        SecurityFinding finding = Assert.Single(findings);
        Assert.Equal(FindingSeverity.High, finding.Severity);
        Assert.Equal(FindingCategory.Firewall, finding.Category);
        Assert.Contains("Public", finding.Title, StringComparison.Ordinal);
        Assert.Equal("Public", finding.Evidence["profile"]);
        Assert.Equal(Now, finding.CapturedAtUtc);
    }

    [Fact]
    public async Task EnabledFlagAsGpoBooleanInteger_IsInterpretedCorrectly()
    {
        // MSFT_NetFirewallProfile.Enabled can surface as uint16: 0/1/2 (NotConfigured)
        SetUpProfiles(Profile("Domain", (ushort)0), Profile("Private", (ushort)1), Profile("Public", (ushort)2));

        IReadOnlyList<SecurityFinding> findings = await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None);

        SecurityFinding finding = Assert.Single(findings);
        Assert.Equal("Domain", finding.Evidence["profile"]);
    }

    [Fact]
    public async Task WmiFailure_ProducesConservativeInfoFindingInsteadOfSilence()
    {
        _wmiQueryService
            .QueryAsync(
                Arg.Any<ScanTarget>(),
                Arg.Any<ScanCredentials>(),
                Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<WmiInstance>>(
                Error.WmiUnavailable("WMI service unreachable")));

        IReadOnlyList<SecurityFinding> findings = await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None);

        SecurityFinding finding = Assert.Single(findings);
        Assert.Equal(FindingSeverity.Info, finding.Severity);
        Assert.Equal("WmiUnavailable", finding.Evidence["errorCode"]);
    }

    [Fact]
    public async Task AccessDenied_CarriesRequiredPrivilegeOnTheFinding()
    {
        _wmiQueryService
            .QueryAsync(
                Arg.Any<ScanTarget>(),
                Arg.Any<ScanCredentials>(),
                Arg.Any<ConnectionOptions>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<WmiInstance>>(
                Error.AccessDenied("access denied", PrivilegeLevel.Administrator)));

        IReadOnlyList<SecurityFinding> findings = await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None);

        SecurityFinding finding = Assert.Single(findings);
        Assert.Equal(PrivilegeLevel.Administrator, finding.RequiredPrivilege);
    }
}
