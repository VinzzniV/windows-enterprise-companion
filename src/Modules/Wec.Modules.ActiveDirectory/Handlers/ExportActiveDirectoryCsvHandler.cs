using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Wec.Core.Abstractions;
using Wec.Core.Messaging;
using Wec.Core.Results;

namespace Wec.Modules.ActiveDirectory.Handlers;

/// <summary>
/// Persists the CSV assembled by the UI after it has retrieved every page of
/// the current directory analysis. Keeping the data assembly in the UI makes
/// the exported rows identical to the data the administrator reviewed.
/// </summary>
public sealed record ExportActiveDirectoryCsvRequest(string Csv);

public sealed record ExportActiveDirectoryCsvResult(bool Cancelled, string? FilePath);

internal sealed class ExportActiveDirectoryCsvHandler
    : IActionHandler<ExportActiveDirectoryCsvRequest, ExportActiveDirectoryCsvResult>
{
    private readonly ISaveFileDialogService _saveFileDialog;
    private readonly IClock _clock;
    private readonly ILogger<ExportActiveDirectoryCsvHandler> _logger;

    public ExportActiveDirectoryCsvHandler(
        ISaveFileDialogService saveFileDialog,
        IClock clock,
        ILogger<ExportActiveDirectoryCsvHandler> logger)
    {
        _saveFileDialog = saveFileDialog;
        _clock = clock;
        _logger = logger;
    }

    public string Module => "activedirectory";

    public string Action => "exportCsv";

    public async Task<Result<ExportActiveDirectoryCsvResult>> HandleAsync(
        ExportActiveDirectoryCsvRequest payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload.Csv))
        {
            return Result.Failure<ExportActiveDirectoryCsvResult>(new Error(
                ErrorCode.InvalidRequest, "Nothing to export."));
        }

        string suggestedFileName = string.Create(
            CultureInfo.InvariantCulture, $"wec-active-directory-{_clock.UtcNow:yyyyMMdd-HHmm}.csv");
        string? targetPath = _saveFileDialog.PromptForSavePath(
            suggestedFileName, "CSV files (*.csv)|*.csv|All files (*.*)|*.*");
        if (targetPath is null)
        {
            return Result.Success(new ExportActiveDirectoryCsvResult(Cancelled: true, FilePath: null));
        }

        try
        {
            // BOM makes umlauts display correctly when the export opens in Excel.
            await File.WriteAllTextAsync(
                targetPath, payload.Csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
                cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Writing Active Directory CSV to {TargetPath} failed", targetPath);
            return Result.Failure<ExportActiveDirectoryCsvResult>(new Error(
                ErrorCode.FileWriteFailed,
                $"The CSV could not be written to '{targetPath}'.")
            {
                Details = exception.Message,
            });
        }

        return Result.Success(new ExportActiveDirectoryCsvResult(Cancelled: false, FilePath: targetPath));
    }
}
