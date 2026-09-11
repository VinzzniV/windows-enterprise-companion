using ClosedXML.Excel;
using Microsoft.Extensions.Options;
using Wec.Core.Abstractions;
using Wec.Core.Results;

namespace Wec.Modules.DeviceCleanup.Application;

internal sealed class DeviceCleanupWorkbookExporter(
    IPingProbe pingProbe,
    IClock clock,
    IOptions<DeviceCleanupOptions> options)
{
    private const int HeaderRow = 7;
    private static readonly string[] Headers =
    [
        "Device",
        "Description",
        "Description source",
        "Classification",
        "Classification basis",
        "AD state",
        "AD last logon (replicated, UTC)",
        "Kaspersky state",
        "Kaspersky last seen (UTC)",
        "opsi state",
        "opsi last seen (UTC)",
        "Nessus state",
        "Nessus last scan (UTC)",
        "WEC Inventory state",
        "WEC Inventory captured (UTC)",
        "Ping status",
        "Ping checked (UTC)",
        "Relevant findings",
    ];

    public async Task<XLWorkbook> CreateAsync(
        DeviceCleanupExportSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        DateTimeOffset pingCheckedAtUtc = clock.UtcNow;
        string[] pingStatuses = await ProbeAsync(snapshot.Candidates, cancellationToken);

        var workbook = new XLWorkbook();
        workbook.Properties.Title = "WEC Device Cleanup";
        workbook.Properties.Subject = "Read-only device cleanup evidence";
        workbook.Style.Font.FontName = "Arial";
        workbook.Style.Font.FontSize = 10;

        IXLWorksheet sheet = workbook.Worksheets.Add("Device Cleanup");
        sheet.ShowGridLines = false;
        sheet.Cell(2, 1).Value = "WEC Device Cleanup";
        sheet.Cell(2, 1).Style.Font.Bold = true;
        sheet.Cell(2, 1).Style.Font.FontSize = 14;
        sheet.Range(2, 1, 2, Headers.Length).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        sheet.Range(2, 1, 2, Headers.Length).Style.Border.BottomBorderColor = XLColor.FromHtml("#94A3B8");

        sheet.Cell(3, 1).Value = "Evidence assessed (UTC)";
        SetTimestamp(sheet.Cell(3, 2), snapshot.AssessedAtUtc);
        sheet.Cell(3, 4).Value = "Ping checked (UTC)";
        SetTimestamp(sheet.Cell(3, 5), pingCheckedAtUtc);
        sheet.Cell(3, 7).Value = "Exported devices";
        sheet.Cell(3, 8).Value = snapshot.Candidates.Count;

        sheet.Cell(4, 1).Value = "Source coverage";
        sheet.Cell(4, 2).Value = string.Join(
            " | ",
            snapshot.Sources.Select(source => $"{source.Source}: {CoverageLabel(source.Availability)}"));
        sheet.Range(4, 2, 4, Headers.Length).Merge();
        sheet.Cell(4, 2).Style.Font.Italic = true;

        sheet.Cell(5, 1).Value = snapshot.SubjectsTruncated
            ? "The configured subject limit was reached. This export is incomplete. No ping response is inconclusive and does not prove that a device is offline or retired."
            : "No ping response is inconclusive and does not prove that a device is offline or retired.";
        sheet.Range(5, 1, 5, Headers.Length).Merge();
        sheet.Cell(5, 1).Style.Font.Italic = true;
        sheet.Cell(5, 1).Style.Font.FontColor = snapshot.SubjectsTruncated
            ? XLColor.FromHtml("#B45309")
            : XLColor.FromHtml("#475569");

        for (int column = 1; column <= Headers.Length; column++)
        {
            sheet.Cell(HeaderRow, column).Value = Headers[column - 1];
        }

        for (int index = 0; index < snapshot.Candidates.Count; index++)
        {
            DeviceCleanupCandidate candidate = snapshot.Candidates[index];
            int row = HeaderRow + index + 1;
            sheet.Cell(row, 1).Value = candidate.Host;
            sheet.Cell(row, 2).Value = candidate.Description ?? "Not available";
            sheet.Cell(row, 3).Value = candidate.DescriptionSource ?? "Not available";
            sheet.Cell(row, 4).Value = ClassificationLabel(candidate.Classification);
            sheet.Cell(row, 5).Value = candidate.ClassificationExplanation;
            sheet.Cell(row, 6).Value = ActiveDirectoryState(candidate);
            SetOptionalTimestamp(sheet.Cell(row, 7), candidate.ActiveDirectoryLastLogonAtUtc);
            sheet.Cell(row, 8).Value = SourceState(candidate.KasperskyExists, "Registered");
            SetOptionalTimestamp(sheet.Cell(row, 9), candidate.KasperskyLastSeenAtUtc);
            sheet.Cell(row, 10).Value = SourceState(candidate.OpsiExists, "Registered");
            SetOptionalTimestamp(sheet.Cell(row, 11), candidate.OpsiLastSeenAtUtc);
            sheet.Cell(row, 12).Value = SourceState(candidate.NessusExists, "Observed");
            SetOptionalTimestamp(sheet.Cell(row, 13), candidate.NessusLastScanAtUtc);
            sheet.Cell(row, 14).Value = candidate.InventoryExists ? "Stored snapshot" : "Not scanned";
            SetOptionalTimestamp(sheet.Cell(row, 15), candidate.InventoryCapturedAtUtc);
            sheet.Cell(row, 16).Value = pingStatuses[index];
            SetTimestamp(sheet.Cell(row, 17), pingCheckedAtUtc);
            sheet.Cell(row, 18).Value = candidate.RelevantFindingCount;
        }

        FormatSheet(sheet, snapshot.Candidates.Count);
        return workbook;
    }

    private async Task<string[]> ProbeAsync(
        IReadOnlyList<DeviceCleanupCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var statuses = new string[candidates.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, candidates.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = options.Value.ExportPingParallelism,
                CancellationToken = cancellationToken,
            },
            async (index, token) =>
            {
                Result<PingProbeReply> result = await pingProbe.SendAsync(
                    candidates[index].Host,
                    TimeSpan.FromMilliseconds(options.Value.ExportPingTimeoutMilliseconds),
                    token);
                statuses[index] = result.IsFailure
                    ? "Ping check failed"
                    : result.Value.Success
                        ? "Ping responded"
                        : "No ping response";
            });
        return statuses;
    }

    private static void FormatSheet(IXLWorksheet sheet, int candidateCount)
    {
        IXLRange header = sheet.Range(HeaderRow, 1, HeaderRow, Headers.Length);
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E3A5F");
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Font.Bold = true;
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        header.Style.Border.RightBorder = XLBorderStyleValues.Thin;
        header.Style.Border.RightBorderColor = XLColor.White;
        sheet.Row(HeaderRow).Height = 30;

        int lastRow = HeaderRow + candidateCount;
        IXLRange table = sheet.Range(HeaderRow, 1, Math.Max(HeaderRow, lastRow), Headers.Length);
        table.SetAutoFilter();
        table.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        table.Style.Border.BottomBorder = XLBorderStyleValues.Hair;
        table.Style.Border.BottomBorderColor = XLColor.FromHtml("#CBD5E1");
        if (candidateCount > 0)
        {
            sheet.Range(HeaderRow + 1, 2, lastRow, 2).Style.Alignment.WrapText = true;
            sheet.Range(HeaderRow + 1, 5, lastRow, 5).Style.Alignment.WrapText = true;
            sheet.Range(HeaderRow + 1, 2, lastRow, 5).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            for (int row = HeaderRow + 1; row <= lastRow; row++)
            {
                if ((row - HeaderRow) % 2 == 0)
                {
                    sheet.Range(row, 1, row, Headers.Length).Style.Fill.BackgroundColor =
                        XLColor.FromHtml("#F8FAFC");
                }

                IXLCell pingCell = sheet.Cell(row, 16);
                pingCell.Style.Font.FontColor = pingCell.GetString() == "Ping responded"
                    ? XLColor.FromHtml("#166534")
                    : XLColor.FromHtml("#9A3412");
            }
        }

        sheet.SheetView.FreezeRows(HeaderRow);
        sheet.SheetView.FreezeColumns(1);
        sheet.Column(1).Width = 28;
        sheet.Column(2).Width = 38;
        sheet.Column(3).Width = 19;
        sheet.Column(4).Width = 20;
        sheet.Column(5).Width = 56;
        sheet.Column(6).Width = 14;
        for (int column = 7; column <= 15; column++)
        {
            sheet.Column(column).Width = column % 2 == 1 ? 24 : 18;
        }

        sheet.Column(16).Width = 20;
        sheet.Column(17).Width = 22;
        sheet.Column(18).Width = 16;
        sheet.Range(3, 1, 5, Headers.Length).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
    }

    private static void SetTimestamp(IXLCell cell, DateTimeOffset timestamp)
    {
        cell.Value = timestamp.UtcDateTime;
        cell.Style.DateFormat.Format = "dd.mm.yyyy hh:mm";
    }

    private static void SetOptionalTimestamp(IXLCell cell, DateTimeOffset? timestamp)
    {
        if (timestamp.HasValue)
        {
            SetTimestamp(cell, timestamp.Value);
        }
        else
        {
            cell.Value = "Not available";
        }
    }

    private static string ClassificationLabel(DeviceCleanupClassification classification) => classification switch
    {
        DeviceCleanupClassification.PotentialCleanup => "Potential cleanup",
        DeviceCleanupClassification.Review => "Review",
        DeviceCleanupClassification.InsufficientEvidence => "Insufficient evidence",
        DeviceCleanupClassification.NoCleanupSignal => "No cleanup signal",
        _ => "Unknown",
    };

    private static string ActiveDirectoryState(DeviceCleanupCandidate candidate)
    {
        if (candidate.ActiveDirectoryExists is null)
        {
            return "Not evaluated";
        }

        if (!candidate.ActiveDirectoryExists.Value)
        {
            return "Not found";
        }

        return candidate.ActiveDirectoryEnabled switch
        {
            true => "Enabled",
            false => "Disabled",
            null => "Present",
        };
    }

    private static string SourceState(bool? exists, string presentLabel) => exists switch
    {
        true => presentLabel,
        false => "Not found",
        null => "Not evaluated",
    };

    private static string CoverageLabel(Wec.Core.Contracts.ActionEvidenceAvailability availability) => availability switch
    {
        Wec.Core.Contracts.ActionEvidenceAvailability.Available => "Available",
        Wec.Core.Contracts.ActionEvidenceAvailability.Partial => "Partial",
        Wec.Core.Contracts.ActionEvidenceAvailability.NotConnected => "Not connected",
        Wec.Core.Contracts.ActionEvidenceAvailability.Unavailable => "Unavailable",
        Wec.Core.Contracts.ActionEvidenceAvailability.Truncated => "Truncated",
        _ => "Unknown",
    };
}
