using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Modules.Reporting.Application;

namespace Wec.Modules.Reporting.Tests.Application;

public sealed class JsonExportTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 7, 2, 19, 0, 0, TimeSpan.Zero);

    private readonly IInventoryReportDataProvider _inventoryProvider =
        Substitute.For<IInventoryReportDataProvider>();

    private readonly ISecurityReportDataProvider _securityProvider =
        Substitute.For<ISecurityReportDataProvider>();

    private readonly ISaveFileDialogService _saveFileDialog = Substitute.For<ISaveFileDialogService>();
    private readonly string _exportPath = Path.Combine(
        Path.GetTempPath(), $"wec-report-test-{Guid.NewGuid():N}.json");

    private ReportExportService CreateService()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new ReportExportService(
            _inventoryProvider,
            _securityProvider,
            _saveFileDialog,
            Substitute.For<IShellLauncher>(),
            clock,
            NullLogger<ReportExportService>.Instance);
    }

    [Fact]
    public async Task JsonExport_WritesParseableDocumentWithTheSameDataSet()
    {
        _inventoryProvider.GetLatestAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new InventoryReportData(
            Now.AddMinutes(-30),
            new CpuReportData("Test CPU", 8, 16, 4000),
            [new MemoryBankReportData("RAM Corp", "R-1", 17179869184, 4800)],
            [new DiskReportData("SSD", 1000204886016, "SCSI")],
            new OperatingSystemReportData("Windows 11 Pro", "10.0", "26200", "64-Bit")));
        _securityProvider.GetLatestScanAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new SecurityReportData(
            Now.AddMinutes(-10),
            "Completed",
            [new SecurityFindingReportData(
                "WEC-SEC-TEST", "Title", "Description", "High", 3, "Firewall",
                "Resource", "Recommendation", null)]));
        _saveFileDialog.PromptForSavePath(Arg.Any<string>(), Arg.Any<string>()).Returns(_exportPath);

        Result<ReportExportResult> result = await CreateService().ExportJsonAsync(
            host: null, openAfterExport: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_exportPath, result.Value.FilePath);

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(_exportPath));
        JsonElement root = document.RootElement;
        Assert.Equal(Environment.MachineName, root.GetProperty("machineName").GetString());
        Assert.Equal("Test CPU", root.GetProperty("inventory").GetProperty("cpu").GetProperty("name").GetString());
        Assert.Equal("High", root.GetProperty("securityScan").GetProperty("findings")[0].GetProperty("severity").GetString());
    }

    [Fact]
    public async Task JsonExport_RendersMissingDataAsNullInsteadOfOmitting()
    {
        _inventoryProvider.GetLatestAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((InventoryReportData?)null);
        _securityProvider.GetLatestScanAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new SecurityReportData(
            Now, "Completed", []));
        _saveFileDialog.PromptForSavePath(Arg.Any<string>(), Arg.Any<string>()).Returns(_exportPath);

        await CreateService().ExportJsonAsync(host: null, openAfterExport: false, CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(_exportPath));
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("inventory").ValueKind);
    }

    [Fact]
    public async Task JsonExport_SuggestsJsonFileName()
    {
        _inventoryProvider.GetLatestAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((InventoryReportData?)null);
        _securityProvider.GetLatestScanAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new SecurityReportData(
            Now, "Completed", []));
        _saveFileDialog.PromptForSavePath(Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);

        await CreateService().ExportJsonAsync(host: null, openAfterExport: false, CancellationToken.None);

        _saveFileDialog.Received(1).PromptForSavePath(
            Arg.Is<string>(name => name.EndsWith(".json", StringComparison.Ordinal)),
            Arg.Is<string>(filter => filter.Contains("*.json", StringComparison.Ordinal)));
    }

    public void Dispose()
    {
        if (File.Exists(_exportPath))
        {
            File.Delete(_exportPath);
        }
    }
}
