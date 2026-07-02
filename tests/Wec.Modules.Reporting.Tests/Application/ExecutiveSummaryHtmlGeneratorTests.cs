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
        findings);

    private static SecurityFindingReportData Finding(
        string findingId,
        string severity,
        int rank,
        string title = "Finding title") => new(
        findingId, title, "Description", severity, rank, "Firewall", "Resource", "Recommendation", null);

    private static ExecutiveSummaryContext Context(
        InventoryReportData? inventory,
        SecurityReportData? scan) => new("TESTHOST", "0.1.0", GeneratedAt, inventory, scan);

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
    public void Generate_ContainsNoExternalReferences()
    {
        string html = ExecutiveSummaryHtmlGenerator.Generate(Context(BuildInventory(), BuildScan(Finding("A", "High", 3))));

        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("src=", html, StringComparison.OrdinalIgnoreCase);
    }
}

