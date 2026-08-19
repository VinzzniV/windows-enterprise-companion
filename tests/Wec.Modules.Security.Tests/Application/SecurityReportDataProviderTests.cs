using NSubstitute;
using Wec.Core.Contracts;
using Wec.Core.Privileges;
using Wec.Core.Results;
using Wec.Modules.Security.Application;
using Wec.Modules.Security.Domain;
using Wec.Modules.Security.Persistence;

namespace Wec.Modules.Security.Tests.Application;

public sealed class SecurityReportDataProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 6, 0, 0, TimeSpan.Zero);

    private readonly ISecurityScanRepository _repository = Substitute.For<ISecurityScanRepository>();

    [Fact]
    public async Task Provider_MapsPersistedCoverageAndSanitizedCheckFailures()
    {
        SecurityFinding finding = new(
            "WEC-SEC-FIREWALL-DISABLED",
            "Firewall disabled",
            "The public profile is disabled.",
            FindingSeverity.High,
            FindingCategory.Firewall,
            "Public",
            new Dictionary<string, string>(),
            "Enable the firewall.",
            RequiredPrivilege: null,
            Now);
        SecurityCheckResult succeeded = SecurityCheckResult.Succeeded("WEC-SEC-FIREWALL", [finding]);
        SecurityCheckResult requiresElevation = new(
            "WEC-SEC-TPM",
            CheckStatus.RequiresElevation,
            [],
            new SecurityCheckFailure(
                ErrorCode.AccessDenied,
                "Administrator privileges are required.",
                PrivilegeLevel.Administrator));
        var scan = new SecurityScanResult(
            1,
            Environment.MachineName,
            Now.AddSeconds(-2),
            Now,
            ScanStatus.Completed,
            [finding],
            [succeeded, requiresElevation],
            SecurityCoverage.CurrentVersion);
        _repository.GetLatestScanAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(scan);

        SecurityReportData? report = await new SecurityReportDataProvider(_repository)
            .GetLatestScanAsync(host: null, CancellationToken.None);

        Assert.NotNull(report);
        Assert.True(report.Coverage.IsKnown);
        Assert.False(report.Coverage.IsComplete);
        Assert.Equal(2, report.Coverage.TotalChecks);
        Assert.Equal(2, report.Coverage.ApplicableChecks);
        Assert.Equal(1, report.Coverage.SucceededChecks);
        Assert.Equal(1, report.Coverage.RequiresElevationChecks);
        SecurityCheckReportData tpm = Assert.Single(
            report.CheckResults, result => result.CheckId == "WEC-SEC-TPM");
        Assert.Equal("RequiresElevation", tpm.Status);
        Assert.Equal("AccessDenied", tpm.Failure?.Code);
        Assert.Equal("Administrator", tpm.Failure?.RequiredPrivilege);
    }

    [Fact]
    public async Task Provider_LeavesLegacyScanCoverageUnknown()
    {
        var legacyScan = new SecurityScanResult(
            1,
            Environment.MachineName,
            Now.AddSeconds(-2),
            Now,
            ScanStatus.Completed,
            []);
        _repository.GetLatestScanAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(legacyScan);

        SecurityReportData? report = await new SecurityReportDataProvider(_repository)
            .GetLatestScanAsync(host: null, CancellationToken.None);

        Assert.NotNull(report);
        Assert.False(report.Coverage.IsKnown);
        Assert.False(report.Coverage.IsComplete);
        Assert.Empty(report.CheckResults);
    }
}
