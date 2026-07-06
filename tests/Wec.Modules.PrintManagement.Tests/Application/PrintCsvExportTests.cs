using Wec.Modules.PrintManagement.Application;
using Wec.Modules.PrintManagement.Domain;

namespace Wec.Modules.PrintManagement.Tests.Application;

public sealed class PrintCsvExportTests
{
    [Fact]
    public void BuildCsv_ContainsHeaderAndOneRowPerQueue()
    {
        var snapshot = new PrintServerSnapshot(
            "PRSRV1", new DateTimeOffset(2026, 7, 3, 8, 0, 0, TimeSpan.Zero),
            [
                new PrinterEntry(
                    "Denkingen-EG", "PR-EG", "Kyocera KX", "8.1.0.0", "IP_10.1.1.20", "10.1.1.20",
                    "EG Flur", null,
                    new PrinterDevice("VCF1234567", "UTAX P-4539i MFP", "PR-EG", "Denkingen", "Idle", 1000,
                    [
                        new TonerSupply("Toner Black", 80, false),
                        new TonerSupply("Waste Toner Box", null, false),
                    ]),
                    null),
                new PrinterEntry(
                    "Queue;mit;Semikolons", null, null, null, null, null, null, null, null, null),
            ]);

        string csv = PrintCsvExport.BuildCsv([snapshot]);
        string[] lines = csv.TrimEnd().Split(Environment.NewLine);

        Assert.Equal(3, lines.Length);
        Assert.StartsWith("Server;Queue;SerialNumber;Location", lines[0], StringComparison.Ordinal);
        Assert.Contains("VCF1234567", lines[1], StringComparison.Ordinal);
        Assert.Contains("Toner Black 80% | Waste Toner Box", lines[1], StringComparison.Ordinal);
        Assert.Contains("\"Queue;mit;Semikolons\"", lines[2], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("plain", "plain")]
    [InlineData("with;semicolon", "\"with;semicolon\"")]
    [InlineData("with\"quote", "\"with\"\"quote\"")]
    public void EscapeCsvField_QuotesOnlyWhenNeeded(string? input, string expected)
    {
        Assert.Equal(expected, PrintCsvExport.EscapeCsvField(input));
    }
}
