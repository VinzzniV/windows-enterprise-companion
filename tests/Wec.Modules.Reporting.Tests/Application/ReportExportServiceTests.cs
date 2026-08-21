using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

    private static SecurityCoverageReportData CompleteCoverage() => new(
        IsKnown: true,
        IsComplete: true,
        TotalChecks: 2,
        ApplicableChecks: 1,
        SucceededChecks: 1,
        FailedChecks: 0,
        RequiresElevationChecks: 0,
        NotApplicableChecks: 1);

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
            Options.Create(new ReportingOptions()),
            NullLogger<ReportExportService>.Instance);
    }

    private void SetUpData(bool inventory = true, bool scan = true)
    {
        _inventoryProvider.GetLatestAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(inventory
            ? new InventoryReportData(
                Now,
                new CpuReportData("CPU", 8, 16, 4000),
                [],
                [],
                new OperatingSystemReportData("Windows 11", "10.0", "26200", "64-Bit"))
            : null);
        _securityProvider.GetLatestScanAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(scan
            ? new SecurityReportData(Now, "Completed", [], CompleteCoverage(), [])
            : null);
    }

    [Fact]
    public async Task Export_WritesReportToUserChosenPathAndReturnsIt()
    {
        SetUpData();
        _saveFileDialog.PromptForSavePath(Arg.Any<string>(), Arg.Any<string>()).Returns(_exportPath);

        Result<ReportExportResult> result = await CreateService().ExportHtmlAsync(
            host: null, openAfterExport: false, CancellationToken.None);

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

        await CreateService().ExportHtmlAsync(host: null, openAfterExport: true, CancellationToken.None);

        _shellLauncher.Received(1).TryOpenPath(_exportPath);
    }

    [Fact]
    public async Task Export_WhenShellOpenFails_ReturnsWrittenPathAndVisibleWarning()
    {
        SetUpData();
        _saveFileDialog.PromptForSavePath(Arg.Any<string>(), Arg.Any<string>()).Returns(_exportPath);
        _shellLauncher.TryOpenPath(_exportPath).Returns(false);

        Result<ReportExportResult> result = await CreateService().ExportHtmlAsync(
            host: null, openAfterExport: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_exportPath, result.Value.FilePath);
        Assert.NotNull(result.Value.OpenError);
        Assert.True(File.Exists(_exportPath));
    }

    [Fact]
    public async Task Export_DialogCancelled_ReturnsCancelledWithoutWriting()
    {
        SetUpData();
        _saveFileDialog.PromptForSavePath(Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);

        Result<ReportExportResult> result = await CreateService().ExportHtmlAsync(
            host: null, openAfterExport: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Cancelled);
        Assert.False(File.Exists(_exportPath));
        _shellLauncher.DidNotReceive().TryOpenPath(Arg.Any<string>());
    }

    [Fact]
    public async Task Export_WithoutAnyData_FailsWithNotFoundBeforeShowingTheDialog()
    {
        SetUpData(inventory: false, scan: false);

        Result<ReportExportResult> result = await CreateService().ExportHtmlAsync(
            host: null, openAfterExport: false, CancellationToken.None);

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

        Result<ReportExportResult> result = await CreateService().ExportHtmlAsync(
            host: null, openAfterExport: false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.FileWriteFailed, result.Error!.Code);
    }

    [Fact]
    public async Task Overview_ReflectsAvailableData()
    {
        SetUpData(inventory: true, scan: false);

        Result<ReportOverview> overview = await CreateService().GetOverviewAsync(host: null, CancellationToken.None);

        Assert.True(overview.IsSuccess);
        Assert.Equal(Environment.MachineName, overview.Value.SubjectHost);
        Assert.Equal(Now, overview.Value.InventoryCapturedAtUtc);
        Assert.Null(overview.Value.SecurityScanCompletedAtUtc);
        Assert.Null(overview.Value.SecurityCoverage);
        Assert.False(overview.Value.Readiness.IsReady);
        Assert.Equal("READY", overview.Value.Readiness.Sources[0].State);
        Assert.Equal("MISSING", overview.Value.Readiness.Sources[1].State);
    }

    [Fact]
    public async Task Overview_ReportsStaleAndIncompleteSourcesWithoutGuessingReadiness()
    {
        SecurityCoverageReportData incompleteCoverage = new(
            true, false, 2, 2, 1, 1, 0, 0);
        _inventoryProvider.GetLatestAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(
            new InventoryReportData(
                Now.AddDays(-2),
                new CpuReportData("CPU", 8, 16, 4000),
                [],
                [],
                new OperatingSystemReportData("Windows 11", "10.0", "26200", "64-Bit")));
        _securityProvider.GetLatestScanAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(
            new SecurityReportData(Now.AddMinutes(-5), "CompletedWithErrors", [], incompleteCoverage, []));

        Result<ReportOverview> overview = await CreateService().GetOverviewAsync(null, CancellationToken.None);

        Assert.False(overview.Value.Readiness.IsReady);
        Assert.Collection(
            overview.Value.Readiness.Sources,
            inventory =>
            {
                Assert.Equal("STALE", inventory.State);
                Assert.Equal(172800, inventory.AgeSeconds);
                Assert.True(inventory.IsComplete);
            },
            security =>
            {
                Assert.Equal("INCOMPLETE", security.State);
                Assert.Equal(300, security.AgeSeconds);
                Assert.False(security.IsComplete);
            });
    }

    [Fact]
    public async Task Overview_FutureTimestamp_IsIncompleteInsteadOfSilentlyClampingAge()
    {
        _inventoryProvider.GetLatestAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(
            new InventoryReportData(
                Now.AddMinutes(5),
                new CpuReportData("CPU", 8, 16, 4000),
                [],
                [],
                new OperatingSystemReportData("Windows 11", "10.0", "26200", "64-Bit")));
        _securityProvider.GetLatestScanAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(
            new SecurityReportData(Now, "Completed", [], CompleteCoverage(), []));

        Result<ReportOverview> overview = await CreateService().GetOverviewAsync(null, CancellationToken.None);

        ReportSourceReadiness inventory = overview.Value.Readiness.Sources[0];
        Assert.Equal("INCOMPLETE", inventory.State);
        Assert.Null(inventory.AgeSeconds);
        Assert.Contains("future", inventory.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.False(overview.Value.Readiness.IsReady);
    }

    [Fact]
    public async Task Overview_ExposesSecurityCoverageWithoutReinterpretingScanStatus()
    {
        SecurityCoverageReportData incompleteCoverage = new(
            IsKnown: true,
            IsComplete: false,
            TotalChecks: 3,
            ApplicableChecks: 3,
            SucceededChecks: 1,
            FailedChecks: 1,
            RequiresElevationChecks: 1,
            NotApplicableChecks: 0);
        _inventoryProvider.GetLatestAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((InventoryReportData?)null);
        _securityProvider.GetLatestScanAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SecurityReportData(Now, "CompletedWithErrors", [], incompleteCoverage, []));

        Result<ReportOverview> overview = await CreateService().GetOverviewAsync(
            host: null, CancellationToken.None);

        Assert.Equal("CompletedWithErrors", overview.Value.SecurityScanStatus);
        Assert.Equal(incompleteCoverage, overview.Value.SecurityCoverage);
        Assert.False(overview.Value.SecurityCoverage!.IsComplete);
    }

    [Fact]
    public async Task Overview_ForRemoteHost_ForwardsHostToProviders()
    {
        SetUpData();

        Result<ReportOverview> overview = await CreateService().GetOverviewAsync(
            host: "PC-42.contoso.local", CancellationToken.None);

        Assert.Equal("PC-42.contoso.local", overview.Value.SubjectHost);
        await _inventoryProvider.Received(1).GetLatestAsync("PC-42.contoso.local", Arg.Any<CancellationToken>());
        await _securityProvider.Received(1).GetLatestScanAsync("PC-42.contoso.local", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Export_ForRemoteHost_NamesTheReportAfterThatHost()
    {
        SetUpData();
        string? suggestedName = null;
        _saveFileDialog.PromptForSavePath(Arg.Do<string>(name => suggestedName = name), Arg.Any<string>())
            .Returns(_exportPath);

        await CreateService().ExportHtmlAsync(host: "PC-42", openAfterExport: false, CancellationToken.None);

        Assert.NotNull(suggestedName);
        Assert.Contains("pc-42", suggestedName, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (File.Exists(_exportPath))
        {
            File.Delete(_exportPath);
        }
    }
}


