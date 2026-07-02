using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Modules.Reporting.Application;

namespace Wec.Modules.Reporting.Tests.Application;

public sealed class ReportExportServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 7, 2, 18, 0, 0, TimeSpan.Zero);

    private readonly IInventoryReportDataProvider _inventoryProvider =
        Substitute.For<IInventoryReportDataProvider>();

    private readonly ISecurityReportDataProvider _securityProvider =
        Substitute.For<ISecurityReportDataProvider>();

    private readonly ISaveFileDialogService _saveFileDialog = Substitute.For<ISaveFileDialogService>();
    private readonly IShellLauncher _shellLauncher = Substitute.For<IShellLauncher>();
    private readonly string _exportPath = Path.Combine(
        Path.GetTempPath(), $"wec-report-test-{Guid.NewGuid():N}.html");

    private ReportExportService CreateService()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new ReportExportService(
            _inventoryProvider,
            _securityProvider,
            _saveFileDialog,
            _shellLauncher,
            clock,
            NullLogger<ReportExportService>.Instance);
    }

    private void SetUpData(bool inventory = true, bool scan = true)
    {
        _inventoryProvider.GetLatestAsync(Arg.Any<CancellationToken>()).Returns(inventory
            ? new InventoryReportData(
                Now,
                new CpuReportData("CPU", 8, 16, 4000),
                [],
                [],
                new OperatingSystemReportData("Windows 11", "10.0", "26200", "64-Bit"))
            : null);
        _securityProvider.GetLatestScanAsync(Arg.Any<CancellationToken>()).Returns(scan
            ? new SecurityReportData(Now, "Completed", [])
            : null);
    }

    [Fact]
    public async Task Export_WritesReportToUserChosenPathAndReturnsIt()
    {
        SetUpData();
        _saveFileDialog.PromptForSavePath(Arg.Any<string>(), Arg.Any<string>()).Returns(_exportPath);

        Result<HtmlExportResult> result = await CreateService().ExportHtmlAsync(
            openAfterExport: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Cancelled);
        Assert.Equal(_exportPath, result.Value.FilePath);
        string written = await File.ReadAllTextAsync(_exportPath);
        Assert.Contains("Executive Summary", written, StringComparison.Ordinal);
        _shellLauncher.DidNotReceive().TryOpenPath(Arg.Any<string>());
    }

    [Fact]
    public async Task Export_WithOpenAfterExport_OpensTheWrittenFileOnly()
    {
        SetUpData();
        _saveFileDialog.PromptForSavePath(Arg.Any<string>(), Arg.Any<string>()).Returns(_exportPath);

        await CreateService().ExportHtmlAsync(openAfterExport: true, CancellationToken.None);

        _shellLauncher.Received(1).TryOpenPath(_exportPath);
    }

    [Fact]
    public async Task Export_DialogCancelled_ReturnsCancelledWithoutWriting()
    {
        SetUpData();
        _saveFileDialog.PromptForSavePath(Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);

        Result<HtmlExportResult> result = await CreateService().ExportHtmlAsync(
            openAfterExport: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Cancelled);
        Assert.False(File.Exists(_exportPath));
        _shellLauncher.DidNotReceive().TryOpenPath(Arg.Any<string>());
    }

    [Fact]
    public async Task Export_WithoutAnyData_FailsWithNotFoundBeforeShowingTheDialog()
    {
        SetUpData(inventory: false, scan: false);

        Result<HtmlExportResult> result = await CreateService().ExportHtmlAsync(
            openAfterExport: false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.NotFound, result.Error!.Code);
        _saveFileDialog.DidNotReceive().PromptForSavePath(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Export_UnwritablePath_FailsWithFileWriteFailed()
    {
        SetUpData();
        string invalidPath = Path.Combine(Path.GetTempPath(), $"missing-dir-{Guid.NewGuid():N}", "report.html");
        _saveFileDialog.PromptForSavePath(Arg.Any<string>(), Arg.Any<string>()).Returns(invalidPath);

        Result<HtmlExportResult> result = await CreateService().ExportHtmlAsync(
            openAfterExport: false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.FileWriteFailed, result.Error!.Code);
    }

    [Fact]
    public async Task Overview_ReflectsAvailableData()
    {
        SetUpData(inventory: true, scan: false);

        Result<ReportOverview> overview = await CreateService().GetOverviewAsync(CancellationToken.None);

        Assert.True(overview.IsSuccess);
        Assert.Equal(Now, overview.Value.InventoryCapturedAtUtc);
        Assert.Null(overview.Value.SecurityScanCompletedAtUtc);
    }

    public void Dispose()
    {
        if (File.Exists(_exportPath))
        {
            File.Delete(_exportPath);
        }
    }
}

