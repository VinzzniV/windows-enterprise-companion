using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Wec.Core.Abstractions;
using Wec.Core.Messaging;
using Wec.Core.Results;

namespace Wec.Modules.PrintManagement.Handlers;

/// <summary>
/// The CSV itself is composed in the UI — it exports exactly the merged devices
/// and columns on screen, so the de-duplication rules live in one place instead
/// of being re-implemented server-side. The handler only owns the file dialog.
/// </summary>
public sealed record ExportPrintCsvRequest(string Csv);

public sealed record ExportPrintCsvResult(bool Cancelled, string? FilePath);

internal sealed class ExportPrintCsvHandler
    : IActionHandler<ExportPrintCsvRequest, ExportPrintCsvResult>
{
    private readonly ISaveFileDialogService _saveFileDialog;
    private readonly IClock _clock;
    private readonly ILogger<ExportPrintCsvHandler> _logger;

    public ExportPrintCsvHandler(
        ISaveFileDialogService saveFileDialog,
        IClock clock,
        ILogger<ExportPrintCsvHandler> logger)
    {
        _saveFileDialog = saveFileDialog;
        _clock = clock;
        _logger = logger;
    }

    public string Module => "printmanagement";

    public string Action => "exportCsv";

    public async Task<Result<ExportPrintCsvResult>> HandleAsync(
        ExportPrintCsvRequest payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload.Csv))
        {
            return Result.Failure<ExportPrintCsvResult>(new Error(
                ErrorCode.InvalidRequest, "Nothing to export."));
        }

        string suggestedFileName = string.Create(
            CultureInfo.InvariantCulture, $"wec-printers-{_clock.UtcNow:yyyyMMdd-HHmm}.csv");
        string? targetPath = _saveFileDialog.PromptForSavePath(
            suggestedFileName, "CSV files (*.csv)|*.csv|All files (*.*)|*.*");
        if (targetPath is null)
        {
            return Result.Success(new ExportPrintCsvResult(Cancelled: true, FilePath: null));
        }

        try
        {
            // BOM: Excel otherwise reads the file as ANSI and mangles umlauts.
            await File.WriteAllTextAsync(
                targetPath, payload.Csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
                cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Writing printer CSV to {TargetPath} failed", targetPath);
            return Result.Failure<ExportPrintCsvResult>(new Error(
                ErrorCode.FileWriteFailed,
                $"The CSV could not be written to '{targetPath}'.")
            {
                Details = exception.Message,
            });
        }

        return Result.Success(new ExportPrintCsvResult(Cancelled: false, FilePath: targetPath));
    }
}

public sealed record OpenDeviceWebUiRequest(string Address);

public sealed record OpenDeviceWebUiResult(bool Opened, string Url);

/// <summary>
/// Opens the device's own web UI in the default browser (ADR 0009 — never
/// inside the WebView). The address must look like a host, not a URL, so the
/// bridge cannot be used to launch arbitrary targets.
/// </summary>
internal sealed class OpenDeviceWebUiHandler : IActionHandler<OpenDeviceWebUiRequest, OpenDeviceWebUiResult>
{
    private readonly IShellLauncher _shellLauncher;

    public OpenDeviceWebUiHandler(IShellLauncher shellLauncher)
    {
        _shellLauncher = shellLauncher;
    }

    public string Module => "printmanagement";

    public string Action => "openDeviceWebUi";

    public Task<Result<OpenDeviceWebUiResult>> HandleAsync(
        OpenDeviceWebUiRequest payload, CancellationToken cancellationToken)
    {
        string address = payload.Address?.Trim() ?? string.Empty;
        if (Uri.CheckHostName(address) == UriHostNameType.Unknown)
        {
            return Task.FromResult(Result.Failure<OpenDeviceWebUiResult>(new Error(
                ErrorCode.InvalidRequest, $"'{payload.Address}' is not a host name or IP address.")));
        }

        string url = $"https://{address}/";
        bool opened = _shellLauncher.TryOpenPath(url);
        return Task.FromResult(Result.Success(new OpenDeviceWebUiResult(opened, url)));
    }
}
