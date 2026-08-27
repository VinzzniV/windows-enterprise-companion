using NSubstitute;
using Wec.Core.Contracts;
using Wec.Modules.Security.Application;
using Wec.Modules.Security.Domain;
using Wec.Modules.Security.Persistence;

namespace Wec.Modules.Security.Tests.Application;

public sealed class SecurityActionEvidenceProviderTests
{
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 8, 27, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LoadStoredAsync_UsesOneBoundedLatestScanReadAndPreservesCoverage()
    {
        ISecurityScanRepository repository = Substitute.For<ISecurityScanRepository>();
        repository.ListLatestScansAsync(3, Arg.Any<CancellationToken>()).Returns(
        [
            CompleteScan("PC-B", FindingSeverity.Medium),
            LegacyScan("PC-A", FindingSeverity.Critical),
            CompleteScan("PC-C", FindingSeverity.Low),
        ]);
        var provider = new SecurityActionEvidenceProvider(repository);

        SecurityActionEvidenceSnapshot snapshot = await provider.LoadStoredAsync(2, CancellationToken.None);

        Assert.True(snapshot.ScansTruncated);
        Assert.Equal(2, snapshot.EvaluatedScanCount);
        Assert.Equal(["PC-A", "PC-B"], snapshot.Findings.Select(finding => finding.Host));
        SecurityActionEvidence legacy = snapshot.Findings.Single(finding => finding.Host == "PC-A");
        Assert.Equal("Critical", legacy.Severity);
        Assert.Equal(ActionEvidenceAvailability.Partial, legacy.Coverage);
        Assert.Contains("predates", legacy.CoverageExplanation, StringComparison.Ordinal);
        SecurityActionEvidence complete = snapshot.Findings.Single(finding => finding.Host == "PC-B");
        Assert.Equal(ActionEvidenceAvailability.Available, complete.Coverage);
        Assert.Contains("All 1", complete.CoverageExplanation, StringComparison.Ordinal);
        await repository.Received(1).ListLatestScansAsync(3, Arg.Any<CancellationToken>());
    }

    private static SecurityScanResult CompleteScan(string host, FindingSeverity severity)
    {
        SecurityFinding finding = Finding(severity);
        return new SecurityScanResult(
            1,
            host,
            CapturedAt.AddMinutes(-1),
            CapturedAt,
            ScanStatus.Completed,
            [finding],
            [SecurityCheckResult.Succeeded("check", [finding])],
            SecurityCoverage.CurrentVersion);
    }

    private static SecurityScanResult LegacyScan(string host, FindingSeverity severity) => new(
        2,
        host,
        CapturedAt.AddMinutes(-1),
        CapturedAt,
        ScanStatus.Completed,
        [Finding(severity)]);

    private static SecurityFinding Finding(FindingSeverity severity) => new(
        "finding-id",
        "Security issue",
        "Observed condition.",
        severity,
        FindingCategory.PlatformIntegrity,
        "Device",
        new Dictionary<string, string>(),
        "Open Security and review the finding.",
        null,
        CapturedAt);
}
