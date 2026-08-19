using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Security.Application.Checks;
using Wec.Modules.Security.Domain;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Wec.Modules.Security.Tests.Application;

public class LocalAdministratorsPartComponentParsingTests
{
    private static WmiInstance Reference(string className, string domain, string name) =>
        new(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [WmiInstance.ClassNameProperty] = className,
            ["Domain"] = domain,
            ["Name"] = name,
        });

    [Fact]
    public void NestedCimInstanceReference_IsParsedWithKind()
    {
        AdminGroupMember? member = LocalAdministratorsCheck.ParseMember(
            Reference("Win32_UserAccount", "TESTHOST", "Admin"));

        Assert.NotNull(member);
        Assert.Equal(@"TESTHOST\Admin", member.QualifiedName);
        Assert.Equal(AdminMemberKind.User, member.Kind);
    }

    [Fact]
    public void NestedGroupReference_IsParsedAsGroup()
    {
        AdminGroupMember? member = LocalAdministratorsCheck.ParseMember(
            Reference("Win32_Group", "CONTOSO", "IT-Admins"));

        Assert.Equal(AdminMemberKind.Group, member!.Kind);
        Assert.Equal("CONTOSO", member.Domain);
    }

    [Fact]
    public void SystemAccountReference_IsParsedAsSystemAccount()
    {
        AdminGroupMember? member = LocalAdministratorsCheck.ParseMember(
            Reference("Win32_SystemAccount", "TESTHOST", "SYSTEM"));

        Assert.Equal(AdminMemberKind.SystemAccount, member!.Kind);
    }

    [Theory]
    [InlineData(@"\\TESTHOST\root\cimv2:Win32_UserAccount.Domain=""TESTHOST"",Name=""Admin""", "Admin", "User")]
    [InlineData(@"Win32_Group.Domain=""CONTOSO"",Name=""Domänen-Admins""", "Domänen-Admins", "Group")]
    public void DmtfReferencePath_IsParsed(string path, string expectedName, string expectedKind)
    {
        AdminGroupMember? member = LocalAdministratorsCheck.ParseMember(path);

        Assert.Equal(expectedName, member!.Name);
        Assert.Equal(Enum.Parse<AdminMemberKind>(expectedKind), member.Kind);
    }

    [Theory]
    [InlineData("Win32_UserAccount (Name = \"Admin\", Domain = \"TESTHOST\")", "TESTHOST", "Admin", "User")]
    [InlineData("Win32_Group (Domain = \"CONTOSO\", Name = \"IT-Admins\")", "CONTOSO", "IT-Admins", "Group")]
    public void ObservedCimDisplayReference_IsParsedRegardlessOfPropertyOrder(
        string reference,
        string expectedDomain,
        string expectedName,
        string expectedKind)
    {
        AdminGroupMember? member = LocalAdministratorsCheck.ParseMember(reference);

        Assert.NotNull(member);
        Assert.Equal(expectedDomain, member.Domain);
        Assert.Equal(expectedName, member.Name);
        Assert.Equal(Enum.Parse<AdminMemberKind>(expectedKind), member.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("garbage without a reference")]
    [InlineData(42)]
    public void UnparseableValues_YieldNull(object? partComponent)
    {
        Assert.Null(LocalAdministratorsCheck.ParseMember(partComponent));
    }

    [Fact]
    public async Task PartiallyParsedReferences_DoNotClaimAnExactMembershipCount()
    {
        var harness = new CheckTestHarness();
        harness.SetUpWmiQuery("Win32_Group ", CheckTestHarness.Instance(
            ("Name", "Administratoren"), ("Domain", "TESTHOST")));
        harness.SetUpWmiQuery("Win32_GroupUser",
            CheckTestHarness.Instance(("PartComponent", (object?)Reference("Win32_UserAccount", "TESTHOST", "Admin"))),
            CheckTestHarness.Instance(("PartComponent", (object?)Reference("Win32_Group", "CONTOSO", "IT-Admins"))),
            CheckTestHarness.Instance(("PartComponent", (object?)"unparseable")));

        var check = new LocalAdministratorsCheck(harness.WmiQueryService, harness.Clock);
        SecurityCheckResult result = await check.EvaluateAsync(
            CheckTestHarness.LocalContext, CancellationToken.None);
        IReadOnlyList<SecurityFinding> findings = result.Findings;

        Assert.Equal(CheckStatus.Failed, result.Status);
        SecurityFinding membership = Assert.Single(findings);
        Assert.Equal("3", membership.Evidence["rawMemberCount"]);
        Assert.Equal("2", membership.Evidence["parsedMemberCount"]);
        Assert.Equal("1", membership.Evidence["unparsedMemberCount"]);
        Assert.False(membership.Evidence.ContainsKey("memberCount"));
        Assert.Contains("incomplete", membership.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"TESTHOST\Admin (local user)", membership.Evidence["members"], StringComparison.Ordinal);
        Assert.Contains(@"CONTOSO\IT-Admins (domain group)", membership.Evidence["members"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllUnparseableReferences_DoNotClaimZeroMembers()
    {
        var harness = new CheckTestHarness();
        harness.SetUpWmiQuery("Win32_Group ", CheckTestHarness.Instance(
            ("Name", "Administrators"), ("Domain", "TESTHOST")));
        harness.SetUpWmiQuery("Win32_GroupUser",
            CheckTestHarness.Instance(("PartComponent", (object?)"unsupported representation")));

        var check = new LocalAdministratorsCheck(harness.WmiQueryService, harness.Clock);
        SecurityCheckResult result = await check.EvaluateAsync(
            CheckTestHarness.LocalContext, CancellationToken.None);
        SecurityFinding membership = Assert.Single(result.Findings);

        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Equal("1", membership.Evidence["rawMemberCount"]);
        Assert.Equal("0", membership.Evidence["parsedMemberCount"]);
        Assert.Equal("1", membership.Evidence["unparsedMemberCount"]);
        Assert.False(membership.Evidence.ContainsKey("memberCount"));
        Assert.DoesNotContain("has 0 members", membership.Title, StringComparison.OrdinalIgnoreCase);
    }
}

public class UacCheckTests
{
    private readonly CheckTestHarness _harness = new();

    private UacCheck CreateCheck() => new(_harness.RegistryReader, _harness.Clock);

    [Fact]
    public async Task UacDisabled_ProducesHighFinding()
    {
        _harness.SetUpRegistryValue("EnableLUA", 0);

        SecurityFinding finding = Assert.Single(
            (await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
        Assert.Equal(FindingSeverity.High, finding.Severity);
    }

    [Fact]
    public async Task SilentElevation_ProducesMediumFinding()
    {
        _harness.SetUpRegistryValue("EnableLUA", 1);
        _harness.SetUpRegistryValue("ConsentPromptBehaviorAdmin", 0);

        SecurityFinding finding = Assert.Single(
            (await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
        Assert.Equal(FindingSeverity.Medium, finding.Severity);
    }

    [Fact]
    public async Task DefaultConfiguration_ProducesNoFindings()
    {
        _harness.SetUpRegistryValue("EnableLUA", 1);
        _harness.SetUpRegistryValue("ConsentPromptBehaviorAdmin", 5);

        Assert.Empty((await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
    }

    [Fact]
    public async Task ConsentPromptReadFailure_ProducesFailedExecution()
    {
        _harness.SetUpRegistryValue("EnableLUA", 1);
        _harness.SetUpRegistryFailure(
            "ConsentPromptBehaviorAdmin",
            Error.WmiUnavailable("registry provider unavailable"));

        SecurityCheckResult result = await CreateCheck().EvaluateAsync(
            CheckTestHarness.LocalContext, CancellationToken.None);

        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Equal(ErrorCode.WmiUnavailable, result.Failure?.Code);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task RemoteTarget_EvaluatesViaRemoteRegistry()
    {
        // The remote read goes through StdRegProv; the check no longer skips remote targets.
        _harness.SetUpRegistryValue("EnableLUA", 0);

        SecurityFinding finding = Assert.Single(
            (await CreateCheck().EvaluateAsync(CheckTestHarness.RemoteContext, CancellationToken.None)).Findings);
        Assert.Equal(FindingSeverity.High, finding.Severity);
        Assert.DoesNotContain("LOCAL-ONLY", finding.FindingId, StringComparison.Ordinal);
    }

}

public class WindowsUpdateRecencyCheckTests
{
    private readonly CheckTestHarness _harness = new();

    private WindowsUpdateRecencyCheck CreateCheck() => new(
        _harness.WmiQueryService, _harness.Clock, MsOptions.Create(new SecurityOptions()));

    [Fact]
    public async Task RecentUpdate_ProducesNoFindings()
    {
        _harness.SetUpWmiQuery("Win32_QuickFixEngineering",
            CheckTestHarness.Instance(("HotFixID", "KB5060000"), ("InstalledOn", "6/20/2026")));

        Assert.Empty((await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
    }

    [Fact]
    public async Task StalePatchLevel_ProducesMediumFinding()
    {
        _harness.SetUpWmiQuery("Win32_QuickFixEngineering",
            CheckTestHarness.Instance(("HotFixID", "KB5031234"), ("InstalledOn", "1/15/2026")),
            CheckTestHarness.Instance(("HotFixID", "KB5029876"), ("InstalledOn", "12/1/2025")));

        SecurityFinding finding = Assert.Single(
            (await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
        Assert.Equal(FindingSeverity.Medium, finding.Severity);
        Assert.Equal("2026-01-15", finding.Evidence["lastInstalledUpdateUtc"]);
    }

    [Fact]
    public async Task NoParseableDates_ProducesFailedExecution()
    {
        _harness.SetUpWmiQuery("Win32_QuickFixEngineering",
            CheckTestHarness.Instance(("HotFixID", "KB1"), ("InstalledOn", "")));

        SecurityCheckResult result = await CreateCheck().EvaluateAsync(
            CheckTestHarness.LocalContext, CancellationToken.None);
        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Equal(ErrorCode.NotFound, result.Failure?.Code);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("7/2/2026")]
    [InlineData("2026-07-02")]
    public void ParseInstalledOn_HandlesCommonFormats(string installedOn)
    {
        Assert.NotNull(WindowsUpdateRecencyCheck.ParseInstalledOn(installedOn));
    }
}

public class RebootPendingCheckTests
{
    private readonly CheckTestHarness _harness = new();

    private RebootPendingCheck CreateCheck() => new(_harness.RegistryReader, _harness.Clock);

    [Fact]
    public async Task NoSignals_ProducesNoFindings()
    {
        _harness.SetUpSubKeys("Component Based Servicing");
        _harness.SetUpSubKeys("Auto Update");
        _harness.SetUpRegistryValue("PendingFileRenameOperations", null);

        Assert.Empty((await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
    }

    [Fact]
    public async Task CbsAndFileRenameSignals_ProduceOneInfoFindingListingBoth()
    {
        _harness.SetUpSubKeys("Component Based Servicing", "RebootPending");
        _harness.SetUpSubKeys("Auto Update");
        _harness.SetUpRegistryValue("PendingFileRenameOperations", new[] { @"\??\C:\old", "" });

        SecurityFinding finding = Assert.Single(
            (await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
        Assert.Equal(FindingSeverity.Info, finding.Severity);
        Assert.Contains("RebootPending", finding.Evidence["signals"], StringComparison.Ordinal);
        Assert.Contains("PendingFileRenameOperations", finding.Evidence["signals"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoteTarget_EvaluatesViaRemoteRegistry()
    {
        _harness.SetUpSubKeys("Component Based Servicing", "RebootPending");
        _harness.SetUpSubKeys("Auto Update");
        _harness.SetUpRegistryValue("PendingFileRenameOperations", null);

        SecurityFinding finding = Assert.Single(
            (await CreateCheck().EvaluateAsync(CheckTestHarness.RemoteContext, CancellationToken.None)).Findings);
        Assert.Equal(FindingSeverity.Info, finding.Severity);
        Assert.DoesNotContain("LOCAL-ONLY", finding.FindingId, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Component Based Servicing")]
    [InlineData("Auto Update")]
    public async Task SubKeyReadFailure_ProducesFailedExecution(string failingPath)
    {
        _harness.SetUpSubKeys("Component Based Servicing");
        _harness.SetUpSubKeys("Auto Update");
        _harness.SetUpSubKeysFailure(failingPath, Error.WmiUnavailable("registry provider unavailable"));
        _harness.SetUpRegistryValue("PendingFileRenameOperations", null);

        SecurityCheckResult result = await CreateCheck().EvaluateAsync(
            CheckTestHarness.LocalContext, CancellationToken.None);

        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task PendingRenameReadFailure_ProducesFailedExecution()
    {
        _harness.SetUpSubKeys("Component Based Servicing");
        _harness.SetUpSubKeys("Auto Update");
        _harness.SetUpRegistryFailure(
            "PendingFileRenameOperations",
            Error.WmiUnavailable("registry provider unavailable"));

        SecurityCheckResult result = await CreateCheck().EvaluateAsync(
            CheckTestHarness.LocalContext, CancellationToken.None);

        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Empty(result.Findings);
    }
}

public class AccountPolicyCheckTests
{
    private readonly ILocalAccountPolicyReader _policyReader = Substitute.For<ILocalAccountPolicyReader>();
    private readonly CheckTestHarness _harness = new();

    private AccountPolicyCheck CreateCheck() => new(
        _policyReader, _harness.Clock, MsOptions.Create(new SecurityOptions()));

    private void SetUpPolicy(int minPasswordLength, int lockoutThreshold) =>
        _policyReader.ReadAccountPolicy().Returns(Result.Success(new LocalAccountPolicy(
            minPasswordLength, TimeSpan.FromDays(42), 24, lockoutThreshold, TimeSpan.FromMinutes(30))));

    [Fact]
    public async Task WeakPasswordLengthAndNoLockout_ProduceTwoMediumFindings()
    {
        SetUpPolicy(minPasswordLength: 0, lockoutThreshold: 0);

        IReadOnlyList<SecurityFinding> findings =
            (await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings;

        Assert.Equal(2, findings.Count);
        Assert.All(findings, finding => Assert.Equal(FindingSeverity.Medium, finding.Severity));
    }

    [Fact]
    public async Task CompliantPolicy_ProducesNoFindings()
    {
        SetUpPolicy(minPasswordLength: 12, lockoutThreshold: 5);

        Assert.Empty((await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
    }

    [Fact]
    public async Task ReadFailure_ProducesFailedExecution()
    {
        _policyReader.ReadAccountPolicy().Returns(
            Result.Failure<LocalAccountPolicy>(new Error(ErrorCode.WmiUnavailable, "api failed")));

        SecurityCheckResult result = await CreateCheck().EvaluateAsync(
            CheckTestHarness.LocalContext, CancellationToken.None);
        Assert.Equal(CheckStatus.Failed, result.Status);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task RemoteTarget_ProducesNotApplicableExecution()
    {
        SecurityCheckResult result = await CreateCheck().EvaluateAsync(
            CheckTestHarness.RemoteContext, CancellationToken.None);
        Assert.Equal(CheckStatus.NotApplicable, result.Status);
        Assert.Equal(ErrorCode.UnsupportedRemoteOperation, result.Failure?.Code);
        Assert.Empty(result.Findings);
    }
}

public class RemoteRegistryChecksTests
{
    private readonly CheckTestHarness _harness = new();

    [Fact]
    public async Task RdpCheckOnRemoteTarget_ReadsRegistryViaStdRegProv()
    {
        _harness.SetUpRegistryValue("fDenyTSConnections", 0);
        var check = new RdpAccessCheck(_harness.RegistryReader, _harness.Clock);

        SecurityFinding finding = Assert.Single(
            (await check.EvaluateAsync(CheckTestHarness.RemoteContext, CancellationToken.None)).Findings);

        Assert.Equal(FindingSeverity.Medium, finding.Severity);
        Assert.DoesNotContain("LOCAL-ONLY", finding.FindingId, StringComparison.Ordinal);
        await _harness.RegistryReader.Received().ReadLocalMachineValueAsync(
            Arg.Is<ScanTarget>(target => !target.IsLocal),
            Arg.Any<ScanCredentials>(),
            Arg.Any<ConnectionOptions>(),
            Arg.Any<string>(),
            "fDenyTSConnections",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SecureBootCheckOnRemoteTarget_ReadsRegistryViaStdRegProv()
    {
        _harness.SetUpRegistryValue("UEFISecureBootEnabled", 0);
        var check = new SecureBootCheck(_harness.RegistryReader, _harness.Clock);

        SecurityFinding finding = Assert.Single(
            (await check.EvaluateAsync(CheckTestHarness.RemoteContext, CancellationToken.None)).Findings);

        Assert.Equal(FindingSeverity.Medium, finding.Severity);
        Assert.DoesNotContain("LOCAL-ONLY", finding.FindingId, StringComparison.Ordinal);
    }
}

public class DefenderSignatureAgeTests
{
    private readonly CheckTestHarness _harness = new();

    private DefenderStatusCheck CreateCheck() => new(
        _harness.WmiQueryService, _harness.Clock, MsOptions.Create(new SecurityOptions()));

    [Fact]
    public async Task StaleSignatures_ProduceMediumFinding()
    {
        _harness.SetUpWmiQuery("MSFT_MpComputerStatus", CheckTestHarness.Instance(
            ("AntivirusEnabled", true),
            ("RealTimeProtectionEnabled", true),
            ("AntivirusSignatureAge", 12u)));

        SecurityFinding finding = Assert.Single(
            (await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
        Assert.Equal(FindingSeverity.Medium, finding.Severity);
        Assert.Equal("12", finding.Evidence["antivirusSignatureAgeDays"]);
    }

    [Fact]
    public async Task FreshSignatures_ProduceNoFindings()
    {
        _harness.SetUpWmiQuery("MSFT_MpComputerStatus", CheckTestHarness.Instance(
            ("AntivirusEnabled", true),
            ("RealTimeProtectionEnabled", true),
            ("AntivirusSignatureAge", 1u)));

        Assert.Empty((await CreateCheck().EvaluateAsync(CheckTestHarness.LocalContext, CancellationToken.None)).Findings);
    }
}
