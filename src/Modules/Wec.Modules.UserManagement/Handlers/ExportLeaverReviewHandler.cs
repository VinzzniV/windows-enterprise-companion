using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Wec.Core.Abstractions;
using Wec.Core.Messaging;
using Wec.Core.Results;

namespace Wec.Modules.UserManagement.Handlers;

public sealed record ExportLeaverReviewRequest(string Markdown);

public sealed record ExportLeaverReviewResult(bool Cancelled, string? FilePath);

internal sealed class ExportLeaverReviewHandler
    : IActionHandler<ExportLeaverReviewRequest, ExportLeaverReviewResult>
{
    internal const int MaximumMarkdownLength = 1_048_576;

    private readonly ISaveFileDialogService _saveFileDialog;
    private readonly IClock _clock;
    private readonly ILogger<ExportLeaverReviewHandler> _logger;

    public ExportLeaverReviewHandler(
        ISaveFileDialogService saveFileDialog,
        IClock clock,
        ILogger<ExportLeaverReviewHandler> logger)
    {
        _saveFileDialog = saveFileDialog;
        _clock = clock;
        _logger = logger;
    }

    public string Module => "usermanagement";

    public string Action => "exportLeaverReview";

    public async Task<Result<ExportLeaverReviewResult>> HandleAsync(
        ExportLeaverReviewRequest payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload.Markdown))
        {
            return Result.Failure<ExportLeaverReviewResult>(new Error(
                ErrorCode.InvalidRequest, "Nothing to export."));
        }

        if (payload.Markdown.Length > MaximumMarkdownLength)
        {
            return Result.Failure<ExportLeaverReviewResult>(new Error(
                ErrorCode.InvalidRequest, "The Leaver review export exceeds the 1 MiB limit."));
        }

        string suggestedFileName = string.Create(
            CultureInfo.InvariantCulture, $"wec-leaver-review-{_clock.UtcNow:yyyyMMdd-HHmm}.md");
        string? targetPath = _saveFileDialog.PromptForSavePath(
            suggestedFileName, "Markdown files (*.md)|*.md|Text files (*.txt)|*.txt|All files (*.*)|*.*");
        if (targetPath is null)
        {
            return Result.Success(new ExportLeaverReviewResult(Cancelled: true, FilePath: null));
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
            _logger.LogWarning(exception, "Writing Leaver review to {TargetPath} failed", targetPath);
            return Result.Failure<ExportLeaverReviewResult>(new Error(
                ErrorCode.FileWriteFailed,
                $"The Leaver review could not be written to '{targetPath}'.")
            {
                Details = exception.Message,
            });
        }

        return Result.Success(new ExportLeaverReviewResult(Cancelled: false, FilePath: targetPath));
    }
}
