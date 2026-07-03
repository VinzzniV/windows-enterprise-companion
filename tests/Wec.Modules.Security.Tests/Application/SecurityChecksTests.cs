using NSubstitute;
using Wec.Core.Privileges;
using Wec.Core.Results;
using Wec.Modules.Security.Application.Checks;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Tests.Application;

public class DefenderStatusCheckTests
{
    private readonly CheckTestHarness _harness = new();

    private DefenderStatusCheck CreateCheck() => new(
        _harness.WmiQueryService,
        _harness.Clock,
        Microsoft.Extensions.Options.Options.Create(new SecurityOptions()));

    [Fact]
    public async Task AntivirusDisabled_ProducesHighFinding()
    {
        _harness.SetUpWmiQuery("MSFT_MpComputerStatus", CheckTestHarness.Instance(
            ("AntivirusEnabled", false), ("RealTimeProtectionEnabled", false)));

        IReadOnlyList<SecurityFinding> findings = await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None);

        SecurityFinding finding = Assert.Single(findings);
        Assert.Equal(FindingSeverity.High, finding.Severity);
        Assert.Equal(FindingCategory.MalwareProtection, finding.Category);
    }

    [Fact]
    public async Task RealTimeProtectionOffWhileEngineOn_ProducesConservativeMediumFinding()
    {
        _harness.SetUpWmiQuery("MSFT_MpComputerStatus", CheckTestHarness.Instance(
            ("AntivirusEnabled", true), ("RealTimeProtectionEnabled", false)));

        IReadOnlyList<SecurityFinding> findings = await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None);

        Assert.Equal(FindingSeverity.Medium, Assert.Single(findings).Severity);
    }

    [Fact]
    public async Task DefenderNamespaceMissing_ProducesInfoNotRunFinding()
    {
        _harness.SetUpWmiFailure("MSFT_MpComputerStatus", Error.WmiUnavailable("namespace not found"));

        IReadOnlyList<SecurityFinding> findings = await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None);

        SecurityFinding finding = Assert.Single(findings);
        Assert.Equal(FindingSeverity.Info, finding.Severity);
        Assert.EndsWith("NOT-RUN", finding.FindingId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EverythingEnabled_ProducesNoFindings()
    {
        _harness.SetUpWmiQuery("MSFT_MpComputerStatus", CheckTestHarness.Instance(
            ("AntivirusEnabled", true), ("RealTimeProtectionEnabled", true)));

        Assert.Empty(await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None));
    }
}

public class Smb1ProtocolCheckTests
{
    private readonly CheckTestHarness _harness = new();

    private Smb1ProtocolCheck CreateCheck() => new(_harness.WmiQueryService, _harness.Clock);

    [Fact]
    public async Task FeatureEnabled_ProducesHighFinding()
    {
        _harness.SetUpWmiQuery("Win32_OptionalFeature", CheckTestHarness.Instance(
            ("Name", "SMB1Protocol"), ("InstallState", 1u)));

        IReadOnlyList<SecurityFinding> findings = await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None);

        Assert.Equal(FindingSeverity.High, Assert.Single(findings).Severity);
    }

    [Fact]
    public async Task FeatureAbsentOrDisabled_ProducesNoFindings()
    {
        _harness.SetUpWmiQuery("Win32_OptionalFeature");

        Assert.Empty(await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None));
    }
}

public class RdpAccessCheckTests
{
    private readonly CheckTestHarness _harness = new();

    private RdpAccessCheck CreateCheck() => new(_harness.RegistryReader, _harness.Clock);

    [Fact]
    public async Task RdpEnabled_ProducesMediumFinding()
    {
        _harness.SetUpRegistryValue("fDenyTSConnections", 0);

        IReadOnlyList<SecurityFinding> findings = await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None);

        Assert.Equal(FindingSeverity.Medium, Assert.Single(findings).Severity);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(null)]
    public async Task RdpDeniedOrUnconfigured_ProducesNoFindings(object? registryValue)
    {
        _harness.SetUpRegistryValue("fDenyTSConnections", registryValue);

        Assert.Empty(await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None));
    }
}

public class BitLockerCheckTests
{
    private readonly CheckTestHarness _harness = new();
    private readonly IPrivilegeContext _privilegeContext = Substitute.For<IPrivilegeContext>();

    private BitLockerCheck CreateCheck() =>
        new(_harness.WmiQueryService, _privilegeContext, _harness.Clock);

    [Fact]
    public async Task Unelevated_ProducesInfoNotRunFindingWithRequiredPrivilege_WithoutWmi()
    {
        _privilegeContext.Satisfies(PrivilegeLevel.Administrator).Returns(false);

        IReadOnlyList<SecurityFinding> findings = await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None);

        SecurityFinding finding = Assert.Single(findings);
        Assert.Equal(FindingSeverity.Info, finding.Severity);
        Assert.Equal(PrivilegeLevel.Administrator, finding.RequiredPrivilege);
        await _harness.WmiQueryService.DidNotReceive().QueryAsync(
            Arg.Any<Wec.Core.Targets.ScanTarget>(),
            Arg.Any<Wec.Core.Targets.ScanCredentials>(),
            Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ElevatedWithUnprotectedVolume_ProducesMediumFinding()
    {
        _privilegeContext.Satisfies(PrivilegeLevel.Administrator).Returns(true);
        _harness.SetUpWmiQuery("Win32_EncryptableVolume",
            CheckTestHarness.Instance(("DriveLetter", "C:"), ("ProtectionStatus", 1u)),
            CheckTestHarness.Instance(("DriveLetter", "D:"), ("ProtectionStatus", 0u)));

        IReadOnlyList<SecurityFinding> findings = await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None);

        SecurityFinding finding = Assert.Single(findings);
        Assert.Equal(FindingSeverity.Medium, finding.Severity);
        Assert.Equal("D:", finding.Evidence["driveLetter"]);
    }
}

public class SecureBootCheckTests
{
    private readonly CheckTestHarness _harness = new();

    private SecureBootCheck CreateCheck() => new(_harness.RegistryReader, _harness.Clock);

    [Fact]
    public async Task SecureBootDisabled_ProducesMediumFinding()
    {
        _harness.SetUpRegistryValue("UEFISecureBootEnabled", 0);

        Assert.Equal(
            FindingSeverity.Medium,
            Assert.Single(await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Severity);
    }

    [Fact]
    public async Task SecureBootStateMissing_ProducesConservativeLowFinding()
    {
        _harness.SetUpRegistryValue("UEFISecureBootEnabled", null);

        Assert.Equal(
            FindingSeverity.Low,
            Assert.Single(await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Severity);
    }

    [Fact]
    public async Task SecureBootEnabled_ProducesNoFindings()
    {
        _harness.SetUpRegistryValue("UEFISecureBootEnabled", 1);

        Assert.Empty(await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None));
    }
}

public class TpmCheckTests
{
    private readonly CheckTestHarness _harness = new();

    private TpmCheck CreateCheck() => new(_harness.WmiQueryService, _harness.Clock);

    [Fact]
    public async Task AccessDenied_ProducesInfoNotRunFindingWithRequiredPrivilege()
    {
        _harness.SetUpWmiFailure("Win32_Tpm",
            Error.AccessDenied("access denied", PrivilegeLevel.Administrator));

        SecurityFinding finding = Assert.Single(await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None));
        Assert.Equal(FindingSeverity.Info, finding.Severity);
        Assert.Equal(PrivilegeLevel.Administrator, finding.RequiredPrivilege);
    }

    [Fact]
    public async Task NoTpmVisible_ProducesConservativeLowFinding()
    {
        _harness.SetUpWmiQuery("Win32_Tpm");

        Assert.Equal(
            FindingSeverity.Low,
            Assert.Single(await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Severity);
    }

    [Fact]
    public async Task TpmPresentButDisabled_ProducesMediumFinding()
    {
        _harness.SetUpWmiQuery("Win32_Tpm", CheckTestHarness.Instance(
            ("IsEnabled_InitialValue", false), ("SpecVersion", "2.0")));

        Assert.Equal(
            FindingSeverity.Medium,
            Assert.Single(await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Severity);
    }

    [Fact]
    public async Task TpmEnabled_ProducesNoFindings()
    {
        _harness.SetUpWmiQuery("Win32_Tpm", CheckTestHarness.Instance(
            ("IsEnabled_InitialValue", true), ("SpecVersion", "2.0")));

        Assert.Empty(await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None));
    }
}

public class OsSupportCheckTests
{
    private readonly CheckTestHarness _harness = new();

    private OsSupportCheck CreateCheck() => new(_harness.WmiQueryService, _harness.Clock);

    [Fact]
    public async Task BuildPastEndOfSupport_ProducesConservativeMediumFinding()
    {
        // Windows 10 22H2 (19045) ended 2025-10-14; harness clock is 2026-07-02
        _harness.SetUpWmiQuery("Win32_OperatingSystem", CheckTestHarness.Instance(
            ("Caption", "Microsoft Windows 10 Pro"), ("BuildNumber", "19045")));

        SecurityFinding finding = Assert.Single(await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None));
        Assert.Equal(FindingSeverity.Medium, finding.Severity);
        Assert.Equal("2025-10-14", finding.Evidence["endOfSupport"]);
    }

    [Fact]
    public async Task SupportedBuild_ProducesNoFindings()
    {
        // Windows 11 25H2 (26200) is supported until 2027 relative to the harness clock
        _harness.SetUpWmiQuery("Win32_OperatingSystem", CheckTestHarness.Instance(
            ("Caption", "Microsoft Windows 11 Pro"), ("BuildNumber", "26200")));

        Assert.Empty(await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None));
    }

    [Fact]
    public async Task UnknownBuild_ProducesInfoFinding()
    {
        _harness.SetUpWmiQuery("Win32_OperatingSystem", CheckTestHarness.Instance(
            ("Caption", "Microsoft Windows Server 2031"), ("BuildNumber", "99999")));

        Assert.Equal(
            FindingSeverity.Info,
            Assert.Single(await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Severity);
    }
}

public class LocalAdministratorsCheckTests
{
    private readonly CheckTestHarness _harness = new();

    private LocalAdministratorsCheck CreateCheck() => new(_harness.WmiQueryService, _harness.Clock);

    private void SetUpGroupAndMembers(params string[] partComponents)
    {
        _harness.SetUpWmiQuery("Win32_Group ", CheckTestHarness.Instance(
            ("Name", "Administrators"), ("Domain", "TESTHOST")));
        _harness.SetUpWmiQuery("Win32_GroupUser", partComponents
            .Select(partComponent => CheckTestHarness.Instance(("PartComponent", (object?)partComponent)))
            .ToArray());
    }

    [Fact]
    public async Task Membership_IsDocumentedAsInfoFinding()
    {
        SetUpGroupAndMembers(
            @"\\TESTHOST\root\cimv2:Win32_UserAccount.Domain=""TESTHOST"",Name=""Admin""",
            @"\\TESTHOST\root\cimv2:Win32_Group.Domain=""CONTOSO"",Name=""IT-Admins""");

        IReadOnlyList<SecurityFinding> findings = await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None);

        SecurityFinding membership = Assert.Single(findings);
        Assert.Equal(FindingSeverity.Info, membership.Severity);
        Assert.Equal("2", membership.Evidence["memberCount"]);
        Assert.Contains(@"TESTHOST\Admin", membership.Evidence["members"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task BroadPrincipalInAdministrators_ProducesAdditionalMediumFinding()
    {
        SetUpGroupAndMembers(
            @"\\TESTHOST\root\cimv2:Win32_UserAccount.Domain=""TESTHOST"",Name=""Admin""",
            @"\\TESTHOST\root\cimv2:Win32_Group.Domain=""CONTOSO"",Name=""Domain Users""");

        IReadOnlyList<SecurityFinding> findings = await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None);

        Assert.Equal(2, findings.Count);
        SecurityFinding risky = Assert.Single(findings, finding => finding.Severity == FindingSeverity.Medium);
        Assert.Contains("Domain Users", risky.Evidence["riskyMembers"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GroupLookupFailure_ProducesInfoNotRunFinding()
    {
        _harness.SetUpWmiFailure("Win32_Group ", Error.WmiUnavailable("WMI unreachable"));

        SecurityFinding finding = Assert.Single(await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None));
        Assert.Equal(FindingSeverity.Info, finding.Severity);
        Assert.EndsWith("NOT-RUN", finding.FindingId, StringComparison.Ordinal);
    }
}
