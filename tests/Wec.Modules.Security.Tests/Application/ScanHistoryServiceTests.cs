using Microsoft.Extensions.Options;
using NSubstitute;
using Wec.Core.Results;
using Wec.Core.Targets;
using Wec.Modules.Security.Application;
using Wec.Modules.Security.Domain;
using Wec.Modules.Security.Persistence;

namespace Wec.Modules.Security.Tests.Application;

public sealed class ScanHistoryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 2, 21, 0, 0, TimeSpan.Zero);

    private readonly ISecurityScanRepository _repository = Substitute.For<ISecurityScanRepository>();

    private ScanHistoryService CreateService() =>
        new(_repository, Options.Create(new SecurityOptions()));

    private static SecurityFinding Finding(
        string findingId,
        string affectedResource,
        FindingSeverity severity = FindingSeverity.Medium) => new(
        findingId,
        $"Title {findingId}",
        "Description",
        severity,
        FindingCategory.Firewall,
        affectedResource,
        new Dictionary<string, string>(),
        "Recommendation",
        RequiredPrivilege: null,
        Now);

    private static SecurityScanResult Scan(long id, params SecurityFinding[] findings) =>
        ScanWithChecks(id, new SecurityCheckResult("TEST-CHECK", CheckStatus.Succeeded, findings));

    private static SecurityScanResult ScanWithChecks(long id, params SecurityCheckResult[] checkResults) =>
        new(
            id,
            Environment.MachineName,
            Now.AddMinutes(-id),
            Now.AddMinutes(-id).AddSeconds(5),
            ScanStatus.Completed,
            checkResults.SelectMany(result => result.Findings).ToList(),
            checkResults,
            SecurityCoverage.CurrentVersion);

    private static SecurityScanResult LegacyScan(long id, params SecurityFinding[] findings) =>
        new(id, Environment.MachineName, Now.AddMinutes(-id), Now.AddMinutes(-id).AddSeconds(5), ScanStatus.Completed, findings);

    private void SetUpScans(params SecurityScanResult[] newestFirst) =>
        _repository.GetRecentScansAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(newestFirst);

    [Fact]
    public async Task SummariesCarrySeverityCountsOrderedBySeverityDescending()
    {
        SetUpScans(Scan(
            1,
            Finding("A", "r1", FindingSeverity.High),
            Finding("B", "r2", FindingSeverity.Info),
            Finding("C", "r3", FindingSeverity.High)));

        Result<ScanHistoryResult> result = await CreateService().GetHistoryAsync(ScanTarget.Local, CancellationToken.None);

        ScanSummary summary = Assert.Single(result.Value.Scans);
        Assert.Equal(3, summary.FindingCount);
        Assert.Equal(
            [new SeverityCount(FindingSeverity.High, 2), new SeverityCount(FindingSeverity.Info, 1)],
            summary.SeverityCounts);
        Assert.True(summary.Coverage.IsKnown);
        Assert.True(summary.Coverage.IsComplete);
    }

    [Fact]
    public async Task SingleScan_HasNoDiff()
    {
        SetUpScans(Scan(1, Finding("A", "r1")));

        Result<ScanHistoryResult> result = await CreateService().GetHistoryAsync(ScanTarget.Local, CancellationToken.None);

        Assert.Null(result.Value.ChangesSinceLastScan);
    }

    [Fact]
    public async Task Diff_ReportsNewAndResolvedFindings()
    {
        SetUpScans(
            Scan(2, Finding("STAYS", "r1"), Finding("NEW", "r2")),
            Scan(1, Finding("STAYS", "r1"), Finding("RESOLVED", "r3")));

        Result<ScanHistoryResult> result = await CreateService().GetHistoryAsync(ScanTarget.Local, CancellationToken.None);

        ScanDiff diff = result.Value.ChangesSinceLastScan!;
        Assert.Equal(2, diff.LatestScanId);
        Assert.Equal(1, diff.PreviousScanId);
        Assert.Equal("NEW", Assert.Single(diff.NewFindings).FindingId);
        Assert.Equal("RESOLVED", Assert.Single(diff.ResolvedFindings).FindingId);
        Assert.True(diff.IsFullyComparable);
        Assert.Empty(diff.UncomparedCheckIds);
    }

    [Fact]
    public async Task Diff_TreatsSameFindingIdOnDifferentResourceAsNew()
    {
        // One check can flag several resources; identity is (FindingId, AffectedResource)
        SetUpScans(
            Scan(2, Finding("FW-OFF", "Profile 'Public'"), Finding("FW-OFF", "Profile 'Private'")),
            Scan(1, Finding("FW-OFF", "Profile 'Public'")));

        Result<ScanHistoryResult> result = await CreateService().GetHistoryAsync(ScanTarget.Local, CancellationToken.None);

        SecurityFinding newFinding = Assert.Single(result.Value.ChangesSinceLastScan!.NewFindings);
        Assert.Equal("Profile 'Private'", newFinding.AffectedResource);
    }

    [Fact]
    public async Task NoScans_YieldEmptyHistory()
    {
        SetUpScans();

        Result<ScanHistoryResult> result = await CreateService().GetHistoryAsync(ScanTarget.Local, CancellationToken.None);

        Assert.Empty(result.Value.Scans);
        Assert.Null(result.Value.ChangesSinceLastScan);
    }

    [Theory]
    [InlineData(CheckStatus.Failed)]
    [InlineData(CheckStatus.RequiresElevation)]
    [InlineData(CheckStatus.NotApplicable)]
    public async Task Diff_CurrentCheckDidNotSucceed_DoesNotClaimPreviousFindingWasResolved(
        CheckStatus latestStatus)
    {
        SetUpScans(
            ScanWithChecks(2, new SecurityCheckResult("FIREWALL", latestStatus, [])),
            ScanWithChecks(1, new SecurityCheckResult(
                "FIREWALL", CheckStatus.Succeeded, [Finding("FIREWALL-OFF", "Public")])));

        Result<ScanHistoryResult> result = await CreateService().GetHistoryAsync(
            ScanTarget.Local, CancellationToken.None);

        ScanDiff diff = result.Value.ChangesSinceLastScan!;
        Assert.Empty(diff.ResolvedFindings);
        Assert.False(diff.IsFullyComparable);
        Assert.Equal(["FIREWALL"], diff.UncomparedCheckIds);
    }

    [Fact]
    public async Task Diff_PreviousCheckFailed_DoesNotClaimCurrentFindingIsNew()
    {
        SetUpScans(
            ScanWithChecks(2, new SecurityCheckResult(
                "FIREWALL", CheckStatus.Succeeded, [Finding("FIREWALL-OFF", "Public")])),
            ScanWithChecks(1, new SecurityCheckResult("FIREWALL", CheckStatus.Failed, [])));

        Result<ScanHistoryResult> result = await CreateService().GetHistoryAsync(
            ScanTarget.Local, CancellationToken.None);

        ScanDiff diff = result.Value.ChangesSinceLastScan!;
        Assert.Empty(diff.NewFindings);
        Assert.False(diff.IsFullyComparable);
        Assert.Equal(["FIREWALL"], diff.UncomparedCheckIds);
    }

    [Fact]
    public async Task Diff_PartialCoverage_StillComparesChecksThatSucceededInBothScans()
    {
        SetUpScans(
            ScanWithChecks(
                2,
                new SecurityCheckResult("FIREWALL", CheckStatus.Succeeded, []),
                new SecurityCheckResult("TPM", CheckStatus.RequiresElevation, [])),
            ScanWithChecks(
                1,
                new SecurityCheckResult(
                    "FIREWALL", CheckStatus.Succeeded, [Finding("FIREWALL-OFF", "Public")]),
                new SecurityCheckResult("TPM", CheckStatus.Succeeded, [Finding("TPM-OFF", "TPM")])));

        Result<ScanHistoryResult> result = await CreateService().GetHistoryAsync(
            ScanTarget.Local, CancellationToken.None);

        ScanDiff diff = result.Value.ChangesSinceLastScan!;
        Assert.Equal("FIREWALL-OFF", Assert.Single(diff.ResolvedFindings).FindingId);
        Assert.DoesNotContain(diff.ResolvedFindings, finding => finding.FindingId == "TPM-OFF");
        Assert.False(diff.IsFullyComparable);
        Assert.Equal(["TPM"], diff.UncomparedCheckIds);
    }

    [Fact]
    public async Task Diff_LegacyCoverageUnknown_ProducesNoChangeClaims()
    {
        SetUpScans(
            Scan(2),
            LegacyScan(1, Finding("FIREWALL-OFF", "Public")));

        Result<ScanHistoryResult> result = await CreateService().GetHistoryAsync(
            ScanTarget.Local, CancellationToken.None);

        ScanDiff diff = result.Value.ChangesSinceLastScan!;
        Assert.Empty(diff.NewFindings);
        Assert.Empty(diff.ResolvedFindings);
        Assert.False(diff.IsFullyComparable);
        Assert.Empty(diff.UncomparedCheckIds);
        Assert.False(result.Value.Scans[1].Coverage.IsKnown);
    }
}
