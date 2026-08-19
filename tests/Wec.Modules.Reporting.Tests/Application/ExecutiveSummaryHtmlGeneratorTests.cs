using Wec.Core.Contracts;
using Wec.Modules.Reporting.Application;

namespace Wec.Modules.Reporting.Tests.Application;

public class ExecutiveSummaryHtmlGeneratorTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 7, 2, 18, 0, 0, TimeSpan.Zero);

    private static InventoryReportData BuildInventory() => new(
        GeneratedAt.AddMinutes(-30),
        new CpuReportData("AMD Ryzen 7 7700X", 8, 16, 4501),
        [new MemoryBankReportData("Corsair", "CMH32", 17179869184, 4800)],
        [new DiskReportData("Samsung SSD 990", 1000204886016, "SCSI")],
        new OperatingSystemReportData("Microsoft Windows 11 Pro", "10.0.26200", "26200", "64-Bit"));

    private static SecurityReportData BuildScan(params SecurityFindingReportData[] findings) => new(
        GeneratedAt.AddMinutes(-10),
        "Completed",
        findings,
        CompleteCoverage(),
        []);

    private static SecurityCoverageReportData CompleteCoverage() => new(
        true, true, 2, 2, 2, 0, 0, 0);

    private static SecurityReportData BuildScanWithCoverage(
        SecurityCoverageReportData coverage,
        IReadOnlyList<SecurityCheckReportData> checkResults,
        params SecurityFindingReportData[] findings) => new(
        GeneratedAt.AddMinutes(-10),
        "Completed",
        findings,
        coverage,
        checkResults);

    private static SecurityFindingReportData Finding(
        string findingId,
        string severity,
        int rank,
        string title = "Finding title") => new(
        findingId, title, "Description", severity, rank, "Firewall", "Resource", "Recommendation", null);

    private static ExecutiveSummaryContext Context(
        InventoryReportData? inventory,
        SecurityReportData? scan) => new(
            "TESTHOST",
            "0.1.0",
            GeneratedAt,
            inventory,
            scan,
            Readiness(inventory, scan));

    private static ReportReadiness Readiness(InventoryReportData? inventory, SecurityReportData? scan) => new(
        GeneratedAt,
        inventory is not null && scan is not null && scan.Coverage.IsComplete,
        [
            new ReportSourceReadiness(
                "Hardware inventory",
                "Persisted WMI/CIM inventory snapshot",
                inventory is null ? "MISSING" : "READY",
                inventory?.CapturedAtUtc,
                inventory is null ? null : 1800,
                inventory is not null,
                inventory is null ? "No inventory data is available." : "Available and current."),
            new ReportSourceReadiness(
                "Security posture",
                "Persisted Security scan and per-check outcomes",
                scan is null ? "MISSING" : scan.Coverage.IsComplete ? "READY" : "INCOMPLETE",
                scan?.CompletedAtUtc,
                scan is null ? null : 600,
                scan?.Coverage.IsComplete == true,
                scan is null ? "No Security data is available." : "Coverage assessment."),
        ]);

    [Fact]
    public void Generate_IsDeterministicForIdenticalInput()
    {
        ExecutiveSummaryContext context = Context(BuildInventory(), BuildScan(Finding("A", "High", 3)));

        Assert.Equal(ExecutiveSummaryHtmlGenerator.Generate(context), ExecutiveSummaryHtmlGenerator.Generate(context));
    }

    [Fact]
    public void Generate_RendersInventoryAndScanData()
    {
        string html = ExecutiveSummaryHtmlGenerator.Generate(Context(BuildInventory(), BuildScan(Finding("A", "High", 3))));

        Assert.Contains("TESTHOST", html, StringComparison.Ordinal);
        Assert.Contains("AMD Ryzen 7 7700X", html, StringComparison.Ordinal);
        Assert.Contains("Microsoft Windows 11 Pro", html, StringComparison.Ordinal);
        Assert.Contains("16.0 GB", html, StringComparison.Ordinal);
        Assert.Contains("931.5 GB", html, StringComparison.Ordinal);
        Assert.Contains("2026-07-02 18:00 UTC", html, StringComparison.Ordinal);
        Assert.Contains("1 findings", html, StringComparison.Ordinal);
        Assert.Contains("Report readiness", html, StringComparison.Ordinal);
        Assert.Contains("Persisted WMI/CIM inventory snapshot", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_SortsFindingsBySeverityDescending()
    {
        string html = ExecutiveSummaryHtmlGenerator.Generate(Context(inventory: null, BuildScan(
            Finding("LOW-1", "Low", 1, "LowTitle"),
            Finding("CRIT-1", "Critical", 4, "CriticalTitle"),
            Finding("MED-1", "Medium", 2, "MediumTitle"))));

        int criticalIndex = html.IndexOf("CriticalTitle", StringComparison.Ordinal);
        int mediumIndex = html.IndexOf("MediumTitle", StringComparison.Ordinal);
        int lowIndex = html.IndexOf("LowTitle", StringComparison.Ordinal);
        Assert.True(criticalIndex < mediumIndex);
        Assert.True(mediumIndex < lowIndex);
    }

    [Fact]
    public void Generate_HtmlEncodesUntrustedValues()
    {
        string html = ExecutiveSummaryHtmlGenerator.Generate(Context(inventory: null, BuildScan(
            Finding("XSS", "High", 3, title: "<script>alert('x')</script>"))));

        Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_RendersExplicitNoDataMessages()
    {
        string html = ExecutiveSummaryHtmlGenerator.Generate(Context(inventory: null, scan: null));

        Assert.Contains("No security scan has been run", html, StringComparison.Ordinal);
        Assert.Contains("No hardware snapshot has been captured", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_CompleteCoverageWithoutFindings_IsTheOnlySecurityPass()
    {
        string html = ExecutiveSummaryHtmlGenerator.Generate(Context(inventory: null, BuildScan()));

        Assert.Contains("Coverage complete", html, StringComparison.Ordinal);
        Assert.Contains("<strong>PASS:</strong>", html, StringComparison.Ordinal);
        Assert.Contains("all applicable checks completed successfully", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ScanWithErrors_IsNotAPassEvenIfStoredCoverageClaimsComplete()
    {
        SecurityReportData inconsistentScan = new(
            GeneratedAt,
            "CompletedWithErrors",
            [],
            CompleteCoverage(),
            []);

        string html = ExecutiveSummaryHtmlGenerator.Generate(Context(inventory: null, inconsistentScan));

        Assert.Contains("This scan is not a PASS", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<strong>PASS:</strong>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_IncompleteCoverageWithoutFindings_IsExplicitlyNotAPassAndListsFailures()
    {
        SecurityCoverageReportData incompleteCoverage = new(
            true, false, 3, 3, 1, 1, 1, 0);
        SecurityCheckReportData failedCheck = new(
            "WEC-SEC-TPM",
            "RequiresElevation",
            new SecurityCheckFailureReportData(
                "AccessDenied",
                "Administrator privileges are required.",
                "Administrator"));

        string html = ExecutiveSummaryHtmlGenerator.Generate(Context(
            inventory: null,
            BuildScanWithCoverage(incompleteCoverage, [failedCheck])));

        Assert.Contains("Coverage incomplete", html, StringComparison.Ordinal);
        Assert.Contains("1 of 3 applicable checks", html, StringComparison.Ordinal);
        Assert.Contains("This scan is not a PASS", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<strong>PASS:</strong>", html, StringComparison.Ordinal);
        Assert.Contains("WEC-SEC-TPM", html, StringComparison.Ordinal);
        Assert.Contains("Administrator privileges are required", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_LegacyCoverageWithoutFindings_IsExplicitlyUnavailableAndNotAPass()
    {
        SecurityCoverageReportData legacyCoverage = new(
            false, false, 0, 0, 0, 0, 0, 0);

        string html = ExecutiveSummaryHtmlGenerator.Generate(Context(
            inventory: null,
            BuildScanWithCoverage(legacyCoverage, [])));

        Assert.Contains("Coverage unavailable", html, StringComparison.Ordinal);
        Assert.Contains("legacy scan", html, StringComparison.Ordinal);
        Assert.Contains("This scan is not a PASS", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<strong>PASS:</strong>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_ContainsNoExternalReferences()
    {
        string html = ExecutiveSummaryHtmlGenerator.Generate(Context(BuildInventory(), BuildScan(Finding("A", "High", 3))));

        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("src=", html, StringComparison.OrdinalIgnoreCase);
    }
}

