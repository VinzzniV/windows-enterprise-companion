using Wec.Core.Results;
using Wec.Modules.Security.Domain;

namespace Wec.Modules.Security.Tests.Domain;

public sealed class SecurityCoverageTests
{
    [Fact]
    public void UnknownCoverage_IsNeverComplete()
    {
        SecurityCoverage coverage = SecurityCoverage.From(
            coverageVersion: null,
            [new SecurityCheckResult("A", CheckStatus.Succeeded, [])]);

        Assert.False(coverage.IsKnown);
        Assert.False(coverage.IsComplete);
        Assert.Equal(0, coverage.TotalChecks);
    }

    [Fact]
    public void KnownCoverageWithoutOutcomes_IsNotComplete()
    {
        SecurityCoverage coverage = SecurityCoverage.From(SecurityCoverage.CurrentVersion, []);

        Assert.True(coverage.IsKnown);
        Assert.False(coverage.IsComplete);
    }

    [Fact]
    public void NotApplicableChecks_AreExcludedFromApplicableDenominator()
    {
        SecurityCoverage coverage = SecurityCoverage.From(
            SecurityCoverage.CurrentVersion,
            [
                new SecurityCheckResult("A", CheckStatus.Succeeded, []),
                new SecurityCheckResult("B", CheckStatus.NotApplicable, []),
            ]);

        Assert.Equal(2, coverage.TotalChecks);
        Assert.Equal(1, coverage.ApplicableChecks);
        Assert.Equal(1, coverage.SucceededChecks);
        Assert.Equal(1, coverage.NotApplicableChecks);
        Assert.True(coverage.IsComplete);
    }

    [Theory]
    [InlineData(CheckStatus.Failed)]
    [InlineData(CheckStatus.RequiresElevation)]
    public void UnexecutedApplicableCheck_MakesCoverageIncomplete(CheckStatus status)
    {
        SecurityCoverage coverage = SecurityCoverage.From(
            SecurityCoverage.CurrentVersion,
            [
                new SecurityCheckResult("A", CheckStatus.Succeeded, []),
                new SecurityCheckResult("B", status, []),
            ]);

        Assert.False(coverage.IsComplete);
        Assert.Equal(2, coverage.ApplicableChecks);
    }
}
