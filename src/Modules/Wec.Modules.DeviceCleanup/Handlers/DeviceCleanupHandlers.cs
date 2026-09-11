using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Wec.Core.Abstractions;
using Wec.Core.Contracts;
using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.DeviceCleanup.Application;

namespace Wec.Modules.DeviceCleanup.Handlers;

internal sealed class ListDeviceCleanupCandidatesHandler(DeviceCleanupService service)
    : IActionHandler<ListDeviceCleanupCandidatesRequest, DeviceCleanupPage>
{
    public string Module => "devicecleanup";

    public string Action => "listCandidates";

    public Task<Result<DeviceCleanupPage>> HandleAsync(
        ListDeviceCleanupCandidatesRequest payload,
        CancellationToken cancellationToken) => service.GetPageAsync(payload, cancellationToken);
}

public sealed record ExportDeviceCleanupAssessmentRequest(string Markdown);

public sealed record ExportDeviceCleanupAssessmentResult(bool Cancelled, string? FilePath);

public sealed record ExportDeviceCleanupWorkbookRequest(
    HygieneActionDirectoryConnection? ActiveDirectory = null,
    HygieneActionKasperskyConnection? Kaspersky = null,
    string? OperationId = null,
    string? Search = null,
    bool IncludeWithoutSignals = false);

public sealed record ExportDeviceCleanupWorkbookResult(
    bool Cancelled,
    string? FilePath,
    int ExportedCount,
    bool SubjectsTruncated);

internal sealed class ExportDeviceCleanupWorkbookHandler(
    DeviceCleanupService service,
    DeviceCleanupWorkbookExporter workbookExporter,
    ISaveFileDialogService saveFileDialog,
    IClock clock,
    ILogger<ExportDeviceCleanupWorkbookHandler> logger)
    : IActionHandler<ExportDeviceCleanupWorkbookRequest, ExportDeviceCleanupWorkbookResult>
{
    public string Module => "devicecleanup";

    public string Action => "exportWorkbook";

    public async Task<Result<ExportDeviceCleanupWorkbookResult>> HandleAsync(
        ExportDeviceCleanupWorkbookRequest payload,
        CancellationToken cancellationToken)
    {
        Result<DeviceCleanupExportSnapshot> snapshotResult = await service.GetExportSnapshotAsync(
            new DeviceCleanupExportQuery(
                payload.ActiveDirectory,
                payload.Kaspersky,
                payload.OperationId,
                payload.Search,
                payload.IncludeWithoutSignals),
            cancellationToken);
        if (snapshotResult.IsFailure)
        {
            return Result.Failure<ExportDeviceCleanupWorkbookResult>(snapshotResult.Error!);
        }

        DeviceCleanupExportSnapshot snapshot = snapshotResult.Value;
        if (snapshot.Candidates.Count == 0)
        {
            return Result.Failure<ExportDeviceCleanupWorkbookResult>(Error.NotFound(
                "No device cleanup candidates match the current filter."));
        }

        string suggestedFileName = string.Create(
            CultureInfo.InvariantCulture,
            $"wec-device-cleanup-{clock.UtcNow:yyyyMMdd-HHmm}.xlsx");
        string? targetPath = saveFileDialog.PromptForSavePath(
            suggestedFileName,
            "Excel workbooks (*.xlsx)|*.xlsx|All files (*.*)|*.*");
        if (targetPath is null)
        {
            return Result.Success(new ExportDeviceCleanupWorkbookResult(
                Cancelled: true,
                FilePath: null,
                ExportedCount: 0,
                SubjectsTruncated: snapshot.SubjectsTruncated));
        }

        try
        {
            using var workbook = await workbookExporter.CreateAsync(snapshot, cancellationToken);
            workbook.SaveAs(targetPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Writing Device Cleanup workbook to {TargetPath} failed", targetPath);
            return Result.Failure<ExportDeviceCleanupWorkbookResult>(new Error(
                ErrorCode.FileWriteFailed,
                $"The Device Cleanup workbook could not be written to '{targetPath}'.")
            {
                Details = exception.Message,
            });
        }

        return Result.Success(new ExportDeviceCleanupWorkbookResult(
            Cancelled: false,
            FilePath: targetPath,
            ExportedCount: snapshot.Candidates.Count,
            SubjectsTruncated: snapshot.SubjectsTruncated));
    }
}

internal sealed class ExportDeviceCleanupAssessmentHandler(
    ISaveFileDialogService saveFileDialog,
    IClock clock,
    ILogger<ExportDeviceCleanupAssessmentHandler> logger)
    : IActionHandler<ExportDeviceCleanupAssessmentRequest, ExportDeviceCleanupAssessmentResult>
{
    internal const int MaximumMarkdownLength = 1_048_576;

    public string Module => "devicecleanup";

    public string Action => "exportAssessment";

    public async Task<Result<ExportDeviceCleanupAssessmentResult>> HandleAsync(
        ExportDeviceCleanupAssessmentRequest payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload.Markdown))
        {
            return Result.Failure<ExportDeviceCleanupAssessmentResult>(new Error(
                ErrorCode.InvalidRequest,
                "Nothing to export."));
        }

        if (payload.Markdown.Length > MaximumMarkdownLength)
        {
            return Result.Failure<ExportDeviceCleanupAssessmentResult>(new Error(
                ErrorCode.InvalidRequest,
                "The device cleanup export exceeds the 1 MiB limit."));
        }

        string suggestedFileName = string.Create(
            CultureInfo.InvariantCulture,
            $"wec-device-cleanup-{clock.UtcNow:yyyyMMdd-HHmm}.md");
        string? targetPath = saveFileDialog.PromptForSavePath(
            suggestedFileName,
            "Markdown files (*.md)|*.md|Text files (*.txt)|*.txt|All files (*.*)|*.*");
        if (targetPath is null)
        {
            return Result.Success(new ExportDeviceCleanupAssessmentResult(true, null));
        }

        try
        {
            await File.WriteAllTextAsync(
                targetPath,
                payload.Markdown,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
                cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Writing device cleanup assessment to {TargetPath} failed", targetPath);
            return Result.Failure<ExportDeviceCleanupAssessmentResult>(new Error(
                ErrorCode.FileWriteFailed,
                $"The device cleanup assessment could not be written to '{targetPath}'.")
            {
                Details = exception.Message,
            });
        }

        return Result.Success(new ExportDeviceCleanupAssessmentResult(false, targetPath));
    }
}
