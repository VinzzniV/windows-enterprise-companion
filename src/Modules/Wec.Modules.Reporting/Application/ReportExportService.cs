using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Results;
using Wec.Core.Targets;

namespace Wec.Modules.Reporting.Application;

public sealed record ReportOverview(
    DateTimeOffset? InventoryCapturedAtUtc,
    DateTimeOffset? SecurityScanCompletedAtUtc,
    string? SecurityScanStatus,
    int? SecurityFindingCount,
    SecurityCoverageReportData? SecurityCoverage,
    ReportReadiness Readiness);

public sealed record ReportSourceReadiness(
    string Source,
    string Provenance,
    string State,
    DateTimeOffset? CapturedAtUtc,
    long? AgeSeconds,
    bool IsComplete,
    string Summary);

public sealed record ReportReadiness(
    DateTimeOffset EvaluatedAtUtc,
    bool IsReady,
    IReadOnlyList<ReportSourceReadiness> Sources);

public sealed record ReportExportResult(bool Cancelled, string? FilePath);

internal sealed partial class ReportExportService
{
    private static readonly JsonSerializerOptions JsonExportOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly IInventoryReportDataProvider _inventoryProvider;
    private readonly ISecurityReportDataProvider _securityProvider;
    private readonly ISaveFileDialogService _saveFileDialog;
    private readonly IShellLauncher _shellLauncher;
    private readonly IClock _clock;
    private readonly ReportingOptions _options;
    private readonly ILogger<ReportExportService> _logger;

    public ReportExportService(
        IInventoryReportDataProvider inventoryProvider,
        ISecurityReportDataProvider securityProvider,
        ISaveFileDialogService saveFileDialog,
        IShellLauncher shellLauncher,
        IClock clock,
        IOptions<ReportingOptions> options,
        ILogger<ReportExportService> logger)
    {
        _inventoryProvider = inventoryProvider;
        _securityProvider = securityProvider;
        _saveFileDialog = saveFileDialog;
        _shellLauncher = shellLauncher;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<ReportOverview>> GetOverviewAsync(string? host, CancellationToken cancellationToken)
    {
        InventoryReportData? inventory = await _inventoryProvider.GetLatestAsync(host, cancellationToken);
        SecurityReportData? scan = await _securityProvider.GetLatestScanAsync(host, cancellationToken);
        ReportReadiness readiness = BuildReadiness(inventory, scan, _clock.UtcNow);

        return Result.Success(new ReportOverview(
            inventory?.CapturedAtUtc,
            scan?.CompletedAtUtc,
            scan?.Status,
            scan?.Findings.Count,
            scan?.Coverage,
            readiness));
    }

    public Task<Result<ReportExportResult>> ExportHtmlAsync(
        string? host, bool openAfterExport, CancellationToken cancellationToken) =>
        ExportAsync(
            ExecutiveSummaryHtmlGenerator.Generate,
            "html",
            "HTML report (*.html)|*.html|All files (*.*)|*.*",
            host,
            openAfterExport,
            cancellationToken);

    public Task<Result<ReportExportResult>> ExportJsonAsync(
        string? host, bool openAfterExport, CancellationToken cancellationToken) =>
        ExportAsync(
            context => JsonSerializer.Serialize(context, JsonExportOptions),
            "json",
            "JSON report (*.json)|*.json|All files (*.*)|*.*",
            host,
            openAfterExport,
            cancellationToken);

    private async Task<Result<ReportExportResult>> ExportAsync(
        Func<ExecutiveSummaryContext, string> renderContent,
        string fileExtension,
        string dialogFilter,
        string? host,
        bool openAfterExport,
        CancellationToken cancellationToken)
    {
        InventoryReportData? inventory = await _inventoryProvider.GetLatestAsync(host, cancellationToken);
        SecurityReportData? scan = await _securityProvider.GetLatestScanAsync(host, cancellationToken);

        if (inventory is null && scan is null)
        {
            return Result.Failure<ReportExportResult>(Error.NotFound(
                "There is nothing to export yet — capture a hardware snapshot or run a security scan first."));
        }

        // null host = the local machine; otherwise the report is about the scanned client
        string subjectName = host is null ? Environment.MachineName : ScanTarget.Remote(host).DisplayName;
        DateTimeOffset generatedAtUtc = _clock.UtcNow;
        ReportReadiness readiness = BuildReadiness(inventory, scan, generatedAtUtc);
        string content = renderContent(new ExecutiveSummaryContext(
            subjectName,
            ResolveAppVersion(),
            generatedAtUtc,
            inventory,
            scan,
            readiness));

        string suggestedFileName = string.Create(
            CultureInfo.InvariantCulture,
            $"wec-report-{subjectName.ToLowerInvariant()}-{generatedAtUtc:yyyyMMdd-HHmm}.{fileExtension}");
        string? targetPath = _saveFileDialog.PromptForSavePath(suggestedFileName, dialogFilter);

        if (targetPath is null)
        {
            LogExportCancelled(fileExtension);
            return Result.Success(new ReportExportResult(Cancelled: true, FilePath: null));
        }

        try
        {
            await File.WriteAllTextAsync(targetPath, content, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Writing report to {TargetPath} failed", targetPath);
            return Result.Failure<ReportExportResult>(new Error(
                ErrorCode.FileWriteFailed,
                $"The report could not be written to '{targetPath}'.")
            {
                Details = exception.Message,
            });
        }

        LogExported(targetPath);

        if (openAfterExport)
        {
            // The path never crosses the bridge inbound: we only open what we just wrote
            _shellLauncher.TryOpenPath(targetPath);
        }

        return Result.Success(new ReportExportResult(Cancelled: false, FilePath: targetPath));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Report export ({FileExtension}) cancelled by the user")]
    private partial void LogExportCancelled(string fileExtension);

    [LoggerMessage(Level = LogLevel.Information, Message = "Report exported to {TargetPath}")]
    private partial void LogExported(string targetPath);

    private static string ResolveAppVersion()
    {
        string informationalVersion = typeof(ReportExportService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";
        return informationalVersion.Split('+')[0];
    }

    private ReportReadiness BuildReadiness(
        InventoryReportData? inventory,
        SecurityReportData? scan,
        DateTimeOffset evaluatedAtUtc)
    {
        ReportSourceReadiness inventoryReadiness = SourceReadiness(
            source: "Hardware inventory",
            provenance: "Persisted WMI/CIM inventory snapshot",
            capturedAtUtc: inventory?.CapturedAtUtc,
            isComplete: inventory is not null,
            maximumAge: _options.MaximumInventoryAge,
            incompleteSummary: null,
            evaluatedAtUtc);

        bool securityComplete = scan is not null
            && scan.Coverage.IsKnown
            && scan.Coverage.IsComplete
            && string.Equals(scan.Status, "Completed", StringComparison.Ordinal);
        string? securityIncompleteSummary = scan is null || securityComplete
            ? null
            : !scan.Coverage.IsKnown
                ? "Per-check coverage is unavailable; the scan cannot be treated as complete."
                : $"Only {scan.Coverage.SucceededChecks} of {scan.Coverage.ApplicableChecks} applicable checks completed successfully.";
        ReportSourceReadiness securityReadiness = SourceReadiness(
            source: "Security posture",
            provenance: "Persisted Security scan and per-check outcomes",
            capturedAtUtc: scan?.CompletedAtUtc,
            isComplete: securityComplete,
            maximumAge: _options.MaximumSecurityScanAge,
            incompleteSummary: securityIncompleteSummary,
            evaluatedAtUtc);

        ReportSourceReadiness[] sources = [inventoryReadiness, securityReadiness];
        return new ReportReadiness(
            evaluatedAtUtc,
            sources.All(source => string.Equals(source.State, "READY", StringComparison.Ordinal)),
            sources);
    }

    private static ReportSourceReadiness SourceReadiness(
        string source,
        string provenance,
        DateTimeOffset? capturedAtUtc,
        bool isComplete,
        TimeSpan maximumAge,
        string? incompleteSummary,
        DateTimeOffset evaluatedAtUtc)
    {
        if (capturedAtUtc is null)
        {
            return new ReportSourceReadiness(
                source,
                provenance,
                "MISSING",
                null,
                null,
                IsComplete: false,
                $"No {source.ToLowerInvariant()} data is available.");
        }

        TimeSpan age = evaluatedAtUtc - capturedAtUtc.Value;
        if (age < TimeSpan.Zero)
        {
            return new ReportSourceReadiness(
                source,
                provenance,
                "INCOMPLETE",
                capturedAtUtc,
                null,
                IsComplete: false,
                "The source timestamp is in the future relative to report generation; data age cannot be trusted.");
        }

        long ageSeconds = (long)age.TotalSeconds;
        if (!isComplete)
        {
            return new ReportSourceReadiness(
                source,
                provenance,
                "INCOMPLETE",
                capturedAtUtc,
                ageSeconds,
                IsComplete: false,
                incompleteSummary ?? $"The {source.ToLowerInvariant()} data is incomplete.");
        }

        if (age > maximumAge)
        {
            return new ReportSourceReadiness(
                source,
                provenance,
                "STALE",
                capturedAtUtc,
                ageSeconds,
                IsComplete: true,
                $"The data is older than the configured {maximumAge.TotalHours:0.#}-hour freshness window.");
        }

        return new ReportSourceReadiness(
            source,
            provenance,
            "READY",
            capturedAtUtc,
            ageSeconds,
            IsComplete: true,
            "Available, complete, and within the configured freshness window.");
    }
}
