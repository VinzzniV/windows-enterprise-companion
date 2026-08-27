using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Wec.Core.Abstractions;
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
