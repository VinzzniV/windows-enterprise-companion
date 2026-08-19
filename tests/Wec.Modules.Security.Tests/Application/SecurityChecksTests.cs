using NSubstitute;
using Wec.Core.Abstractions;
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

        IReadOnlyList<SecurityFinding> findings = (await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings;

        SecurityFinding finding = Assert.Single(findings);
        Assert.Equal(FindingSeverity.High, finding.Severity);
        Assert.Equal(FindingCategory.MalwareProtection, finding.Category);
    }

    [Fact]
    public async Task RealTimeProtectionOffWhileEngineOn_ProducesConservativeMediumFinding()
    {
        _harness.SetUpWmiQuery("MSFT_MpComputerStatus", CheckTestHarness.Instance(
            ("AntivirusEnabled", true), ("RealTimeProtectionEnabled", false)));

        IReadOnlyList<SecurityFinding> findings = (await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings;

        Assert.Equal(FindingSeverity.Medium, Assert.Single(findings).Severity);
    }

    [Fact]
    public async Task DefenderNamespaceMissing_ProducesFailedExecution()
    {
        _harness.SetUpWmiFailure("MSFT_MpComputerStatus", Error.WmiUnavailable("namespace not found"));

        SecurityCheckResult result = await CreateCheck().EvaluateAsync(
            CheckTestHarness.LocalContext, CancellationToken.None);

        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Equal(ErrorCode.WmiUnavailable, result.Failure?.Code);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task SecurityCenterFailure_DoesNotSilentlyFallBackToDefender()
    {
        _harness.WmiQueryService
            .QueryAsync(
                Arg.Any<Wec.Core.Targets.ScanTarget>(),
                Arg.Any<Wec.Core.Targets.ScanCredentials>(),
                Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
                @"root\SecurityCenter2",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<WmiInstance>>(
                Error.WmiUnavailable("Security Center unavailable")));
        _harness.SetUpWmiQuery("MSFT_MpComputerStatus", CheckTestHarness.Instance(
            ("AntivirusEnabled", true), ("RealTimeProtectionEnabled", true)));

        SecurityCheckResult result = await CreateCheck().EvaluateAsync(
            CheckTestHarness.LocalContext, CancellationToken.None);

        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Equal(ErrorCode.WmiUnavailable, result.Failure?.Code);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task EverythingEnabled_ProducesNoFindings()
    {
        _harness.SetUpWmiQuery("MSFT_MpComputerStatus", CheckTestHarness.Instance(
            ("AntivirusEnabled", true), ("RealTimeProtectionEnabled", true)));

        Assert.Empty((await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
    }

    [Fact]
    public async Task ActiveThirdPartyAntivirus_SuppressesDefenderFindingWithInfo()
    {
        // SecurityCenter2 reports Kaspersky active; Defender is passive (disabled) — expected, not a finding.
        _harness.SetUpWmiQuery("AntiVirusProduct", CheckTestHarness.Instance(
            ("displayName", "Kaspersky Endpoint Security"), ("productState", 0x061100)));
        _harness.SetUpWmiQuery("MSFT_MpComputerStatus", CheckTestHarness.Instance(
            ("AntivirusEnabled", false), ("RealTimeProtectionEnabled", false)));

        IReadOnlyList<SecurityFinding> findings = (await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings;

        SecurityFinding finding = Assert.Single(findings);
        Assert.Equal(FindingSeverity.Info, finding.Severity);
        Assert.Contains("Kaspersky", finding.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledThirdPartyAntivirus_DoesNotSuppressDefenderCheck()
    {
        // Kaspersky installed but off (productState scanner byte 0x00): fall through to Defender.
        _harness.SetUpWmiQuery("AntiVirusProduct", CheckTestHarness.Instance(
            ("displayName", "Kaspersky Endpoint Security"), ("productState", 0x060000)));
        _harness.SetUpWmiQuery("MSFT_MpComputerStatus", CheckTestHarness.Instance(
            ("AntivirusEnabled", false), ("RealTimeProtectionEnabled", false)));

        IReadOnlyList<SecurityFinding> findings = (await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings;

        Assert.Equal(FindingSeverity.High, Assert.Single(findings).Severity);
    }
}

public class SecurityCenterProductsTests
{
    [Theory]
    [InlineData(0x061100, true)]   // enabled + up to date (Kaspersky, real-time on)
    [InlineData(0x061000, true)]   // enabled but signatures out of date
    [InlineData(0x060000, false)]  // installed but off
    [InlineData(0x060100, false)]  // snoozed (scanner byte 0x01, not 0x10)
    public void IsEnabled_DecodesScannerByte(int productState, bool expected) =>
        Assert.Equal(expected, SecurityCenterProducts.IsEnabled(productState));

    [Fact]
    public void IsEnabled_NullProductState_IsFalse() =>
        Assert.False(SecurityCenterProducts.IsEnabled(null));
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

        IReadOnlyList<SecurityFinding> findings = (await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings;

        Assert.Equal(FindingSeverity.High, Assert.Single(findings).Severity);
    }

    [Fact]
    public async Task FeatureAbsentOrDisabled_ProducesNoFindings()
    {
        _harness.SetUpWmiQuery("Win32_OptionalFeature");

        Assert.Empty((await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
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

        IReadOnlyList<SecurityFinding> findings = (await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings;

        Assert.Equal(FindingSeverity.Medium, Assert.Single(findings).Severity);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(null)]
    public async Task RdpDeniedOrUnconfigured_ProducesNoFindings(object? registryValue)
    {
        _harness.SetUpRegistryValue("fDenyTSConnections", registryValue);

        Assert.Empty((await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
    }
}

public class BitLockerCheckTests
{
    private readonly CheckTestHarness _harness = new();
    private readonly IDiskEncryptionStatusReader _reader = Substitute.For<IDiskEncryptionStatusReader>();

    private BitLockerCheck CreateCheck() =>
        new(_reader, _harness.Clock);

    private void SetUpVolumes(params DiskEncryptionVolume[] volumes) =>
        _reader.ReadAsync(
                Arg.Any<Wec.Core.Targets.ScanTarget>(),
                Arg.Any<Wec.Core.Targets.ScanCredentials>(),
                Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DiskEncryptionVolume>>(volumes));

    [Fact]
    public async Task AccessDenied_ProducesRequiresElevation()
    {
        _reader.ReadAsync(
                Arg.Any<Wec.Core.Targets.ScanTarget>(),
                Arg.Any<Wec.Core.Targets.ScanCredentials>(),
                Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<DiskEncryptionVolume>>(
                Error.AccessDenied("elevation required", PrivilegeLevel.Administrator)));

        SecurityCheckResult result = await CreateCheck().EvaluateAsync(
            CheckTestHarness.LocalContext, CancellationToken.None);

        Assert.Equal(CheckStatus.RequiresElevation, result.Status);
        Assert.Equal(PrivilegeLevel.Administrator, result.Failure?.RequiredPrivilege);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task UnprotectedVolume_ProducesMediumFinding()
    {
        SetUpVolumes(
            new DiskEncryptionVolume("C:", DiskEncryptionProtectionStatus.Protected),
            new DiskEncryptionVolume("D:", DiskEncryptionProtectionStatus.Unprotected));

        SecurityCheckResult result = await CreateCheck().EvaluateAsync(
            CheckTestHarness.LocalContext, CancellationToken.None);

        Assert.Equal(CheckStatus.Succeeded, result.Status);
        SecurityFinding finding = Assert.Single(result.Findings);
        Assert.Equal(FindingSeverity.Medium, finding.Severity);
        Assert.Equal("D:", finding.Evidence["driveLetter"]);
    }

    [Fact]
    public async Task EmptyProviderResult_IsFailedCoverage()
    {
        SetUpVolumes();

        SecurityCheckResult result = await CreateCheck().EvaluateAsync(
            CheckTestHarness.LocalContext, CancellationToken.None);

        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task UnknownVolumeState_IsFailedCoverage()
    {
        SetUpVolumes(new DiskEncryptionVolume("C:", DiskEncryptionProtectionStatus.Unknown));

        SecurityCheckResult result = await CreateCheck().EvaluateAsync(
            CheckTestHarness.LocalContext, CancellationToken.None);

        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task MixedUnknownAndUnprotected_RetainsObservedFindingButFailsCoverage()
    {
        SetUpVolumes(
            new DiskEncryptionVolume("C:", DiskEncryptionProtectionStatus.Unknown),
            new DiskEncryptionVolume("D:", DiskEncryptionProtectionStatus.Unprotected));

        SecurityCheckResult result = await CreateCheck().EvaluateAsync(
            CheckTestHarness.LocalContext, CancellationToken.None);

        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Equal("D:", Assert.Single(result.Findings).Evidence["driveLetter"]);
    }

    [Fact]
    public async Task ProviderFailure_IsFailedCoverage()
    {
        _reader.ReadAsync(
                Arg.Any<Wec.Core.Targets.ScanTarget>(),
                Arg.Any<Wec.Core.Targets.ScanCredentials>(),
                Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<DiskEncryptionVolume>>(
                Error.WmiUnavailable("provider failure")));

        SecurityCheckResult result = await CreateCheck().EvaluateAsync(
            CheckTestHarness.LocalContext, CancellationToken.None);

        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Empty(result.Findings);
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
            Assert.Single((await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings).Severity);
    }

    [Fact]
    public async Task SecureBootStateMissing_ProducesConservativeLowFinding()
    {
        _harness.SetUpRegistryValue("UEFISecureBootEnabled", null);

        Assert.Equal(
            FindingSeverity.Low,
            Assert.Single((await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings).Severity);
    }

    [Fact]
    public async Task SecureBootEnabled_ProducesNoFindings()
    {
        _harness.SetUpRegistryValue("UEFISecureBootEnabled", 1);

        Assert.Empty((await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
    }
}

public class TpmCheckTests
{
    private readonly CheckTestHarness _harness = new();

    private TpmCheck CreateCheck() => new(_harness.WmiQueryService, _harness.Clock);

    [Fact]
    public async Task AccessDenied_ProducesRequiresElevationExecution()
    {
        _harness.SetUpWmiFailure("Win32_Tpm",
            Error.AccessDenied("access denied", PrivilegeLevel.Administrator));

        SecurityCheckResult result = await CreateCheck().EvaluateAsync(
            CheckTestHarness.LocalContext, CancellationToken.None);
        Assert.Equal(CheckStatus.RequiresElevation, result.Status);
        Assert.Equal(PrivilegeLevel.Administrator, result.Failure?.RequiredPrivilege);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task NoTpmVisible_ProducesConservativeLowFinding()
    {
        _harness.SetUpWmiQuery("Win32_Tpm");

        Assert.Equal(
            FindingSeverity.Low,
            Assert.Single((await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings).Severity);
    }

    [Fact]
    public async Task TpmPresentButDisabled_ProducesMediumFinding()
    {
        _harness.SetUpWmiQuery("Win32_Tpm", CheckTestHarness.Instance(
            ("IsEnabled_InitialValue", false), ("SpecVersion", "2.0")));

        Assert.Equal(
            FindingSeverity.Medium,
            Assert.Single((await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings).Severity);
    }

    [Fact]
    public async Task TpmEnabled_ProducesNoFindings()
    {
        _harness.SetUpWmiQuery("Win32_Tpm", CheckTestHarness.Instance(
            ("IsEnabled_InitialValue", true), ("SpecVersion", "2.0")));

        Assert.Empty((await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
    }
}

public class OsSupportCheckTests
{
    private readonly CheckTestHarness _harness = new();

    private OsSupportCheck CreateCheck() => new(_harness.WmiQueryService, _harness.Clock);

    private void SetUpOperatingSystem(string caption, string build, object? sku, int? productType = 1) =>
        _harness.SetUpWmiQuery("Win32_OperatingSystem", CheckTestHarness.Instance(
            ("Caption", caption),
            ("BuildNumber", build),
            ("OperatingSystemSKU", sku),
            ("ProductType", productType)));

    [Fact]
    public async Task BuildPastEndOfSupport_ProducesConservativeMediumFinding()
    {
        // Windows 10 22H2 (19045) ended 2025-10-14; harness clock is 2026-07-02
        SetUpOperatingSystem("Microsoft Windows 10 Pro", "19045", 48u);

        SecurityFinding finding = Assert.Single((await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
        Assert.Equal(FindingSeverity.Medium, finding.Severity);
        Assert.Equal("2025-10-14", finding.Evidence["endOfSupport"]);
    }

    [Fact]
    public async Task SupportedBuild_ProducesNoFindings()
    {
        // Windows 11 25H2 (26200) is supported until 2027 relative to the harness clock
        SetUpOperatingSystem("Microsoft Windows 11 Pro", "26200", 48u);

        Assert.Empty((await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
    }

    [Fact]
    public async Task UnknownBuild_ProducesFailedExecution()
    {
        SetUpOperatingSystem("Microsoft Windows 11 Pro", "99999", 48u);

        SecurityCheckResult result = await CreateCheck().EvaluateAsync(
            CheckTestHarness.LocalContext, CancellationToken.None);

        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Equal(ErrorCode.NotFound, result.Failure?.Code);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task Windows10Build19044_UsesDifferentGaAndLtscDates()
    {
        SetUpOperatingSystem("Microsoft Windows 10 Pro", "19044", 48u);
        SecurityFinding proFinding = Assert.Single(
            (await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
        Assert.Equal("2023-06-13", proFinding.Evidence["endOfSupport"]);
        Assert.Equal("HomePro", proFinding.Evidence["lifecycleTrack"]);

        SetUpOperatingSystem("Microsoft Windows 10 Enterprise LTSC 2021", "19044", 125u);
        Assert.Empty((await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
    }

    [Fact]
    public async Task Windows11Build26100_UsesEnterpriseRatherThanHomeProDate()
    {
        _harness.Clock.UtcNow.Returns(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));

        SetUpOperatingSystem("Microsoft Windows 11 Pro", "26100", 48u);
        SecurityFinding proFinding = Assert.Single(
            (await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
        Assert.Equal("2026-10-13", proFinding.Evidence["endOfSupport"]);

        SetUpOperatingSystem("Microsoft Windows 11 Enterprise", "26100", 4u);
        Assert.Empty((await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
    }

    [Fact]
    public async Task Windows11Build26100_UsesEnterpriseLtscDate()
    {
        _harness.Clock.UtcNow.Returns(new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero));
        SetUpOperatingSystem("Microsoft Windows 11 Enterprise LTSC 2024", "26100", 125u);

        Assert.Empty((await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
    }

    [Fact]
    public async Task Windows11Build26100_UsesIotLtscExtendedDate()
    {
        _harness.Clock.UtcNow.Returns(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        SetUpOperatingSystem("Microsoft Windows 11 IoT Enterprise LTSC 2024", "26100", 191u);

        Assert.Empty((await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
    }

    [Theory]
    [InlineData(null, 1, "(missing)")]
    [InlineData(999u, 1, "999")]
    [InlineData(48u, null, "48")]
    [InlineData(48u, 3, "48")]
    public async Task UnknownOrNonClientEdition_DoesNotPass(
        object? sku,
        int? productType,
        string expectedSkuEvidence)
    {
        SetUpOperatingSystem("Unclassified Windows", "26100", sku, productType);

        SecurityCheckResult result = await CreateCheck().EvaluateAsync(
            CheckTestHarness.LocalContext, CancellationToken.None);

        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Equal(ErrorCode.NotFound, result.Failure?.Code);
        Assert.Contains(
            expectedSkuEvidence == "(missing)" ? "missing" : expectedSkuEvidence,
            result.Failure?.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task MissingBuild_DoesNotPass()
    {
        SetUpOperatingSystem("Microsoft Windows 11 Pro", string.Empty, 48u);

        SecurityCheckResult result = await CreateCheck().EvaluateAsync(
            CheckTestHarness.LocalContext, CancellationToken.None);

        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Contains("build number", result.Failure?.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task EndDateRemainsSupportedUntilPacificDayEnds()
    {
        SetUpOperatingSystem("Microsoft Windows 11 Pro", "26100", 48u);
        _harness.Clock.UtcNow.Returns(new DateTimeOffset(2026, 10, 14, 6, 59, 0, TimeSpan.Zero));

        Assert.Empty((await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);

        _harness.Clock.UtcNow.Returns(new DateTimeOffset(2026, 10, 14, 7, 1, 0, TimeSpan.Zero));
        SecurityFinding finding = Assert.Single(
            (await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
        Assert.EndsWith("-EOL", finding.FindingId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryRequestsEditionAndProductType()
    {
        SetUpOperatingSystem("Microsoft Windows 11 Pro", "26200", 48u);

        await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None);

        await _harness.WmiQueryService.Received().QueryAsync(
            Arg.Any<Wec.Core.Targets.ScanTarget>(),
            Arg.Any<Wec.Core.Targets.ScanCredentials>(),
            Arg.Any<Wec.Core.Targets.ConnectionOptions>(),
            Arg.Any<string>(),
            Arg.Is<string>(query => query.Contains("OperatingSystemSKU", StringComparison.Ordinal)
                && query.Contains("ProductType", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
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

        IReadOnlyList<SecurityFinding> findings = (await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings;

        SecurityFinding membership = Assert.Single(findings);
        Assert.Equal(FindingSeverity.Info, membership.Severity);
        Assert.Equal("2", membership.Evidence["memberCount"]);
        Assert.Equal("2", membership.Evidence["rawMemberCount"]);
        Assert.Equal("2", membership.Evidence["parsedMemberCount"]);
        Assert.Equal("0", membership.Evidence["unparsedMemberCount"]);
        Assert.Contains(@"TESTHOST\Admin", membership.Evidence["members"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyRawMembership_IsTheOnlyDefinitiveZero()
    {
        SetUpGroupAndMembers();

        SecurityFinding membership = Assert.Single(
            (await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);

        Assert.Equal("Local Administrators group has 0 members", membership.Title);
        Assert.Equal("0", membership.Evidence["memberCount"]);
        Assert.Equal("0", membership.Evidence["rawMemberCount"]);
        Assert.Equal("0", membership.Evidence["unparsedMemberCount"]);
    }

    [Fact]
    public async Task BroadPrincipalInAdministrators_ProducesAdditionalMediumFinding()
    {
        SetUpGroupAndMembers(
            @"\\TESTHOST\root\cimv2:Win32_UserAccount.Domain=""TESTHOST"",Name=""Admin""",
            @"\\TESTHOST\root\cimv2:Win32_Group.Domain=""CONTOSO"",Name=""Domain Users""");

        IReadOnlyList<SecurityFinding> findings = (await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings;

        Assert.Equal(2, findings.Count);
        SecurityFinding risky = Assert.Single(findings, finding => finding.Severity == FindingSeverity.Medium);
        Assert.Contains("Domain Users", risky.Evidence["riskyMembers"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task GroupLookupFailure_ProducesFailedExecution()
    {
        _harness.SetUpWmiFailure("Win32_Group ", Error.WmiUnavailable("WMI unreachable"));

        SecurityCheckResult result = await CreateCheck().EvaluateAsync(
            CheckTestHarness.LocalContext, CancellationToken.None);
        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Equal(ErrorCode.WmiUnavailable, result.Failure?.Code);
        Assert.Empty(result.Findings);
    }
}
