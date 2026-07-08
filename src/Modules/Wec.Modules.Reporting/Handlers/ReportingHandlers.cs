using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.Reporting.Application;

namespace Wec.Modules.Reporting.Handlers;

/// <param name="Host">null = the local machine; otherwise the scanned remote client.</param>
public sealed record GetReportOverviewRequest(string? Host = null);

internal sealed class GetReportOverviewHandler : IActionHandler<GetReportOverviewRequest, ReportOverview>
{
    private readonly ReportExportService _reportExportService;

    public GetReportOverviewHandler(ReportExportService reportExportService)
    {
        _reportExportService = reportExportService;
    }

    public string Module => "reporting";

    public string Action => "getOverview";

    public Task<Result<ReportOverview>> HandleAsync(
        GetReportOverviewRequest payload,
        CancellationToken cancellationToken) =>
        _reportExportService.GetOverviewAsync(payload.Host, cancellationToken);
}

public sealed record ExportHtmlReportRequest(bool OpenAfterExport = false, string? Host = null);

internal sealed class ExportHtmlReportHandler : IActionHandler<ExportHtmlReportRequest, ReportExportResult>
{
    private readonly ReportExportService _reportExportService;

    public ExportHtmlReportHandler(ReportExportService reportExportService)
    {
        _reportExportService = reportExportService;
    }

    public string Module => "reporting";

    public string Action => "exportHtml";

    public Task<Result<ReportExportResult>> HandleAsync(
        ExportHtmlReportRequest payload,
        CancellationToken cancellationToken) =>
        _reportExportService.ExportHtmlAsync(payload.Host, payload.OpenAfterExport, cancellationToken);
}

public sealed record ExportJsonReportRequest(bool OpenAfterExport = false, string? Host = null);

internal sealed class ExportJsonReportHandler : IActionHandler<ExportJsonReportRequest, ReportExportResult>
{
    private readonly ReportExportService _reportExportService;

    public ExportJsonReportHandler(ReportExportService reportExportService)
    {
        _reportExportService = reportExportService;
    }

    public string Module => "reporting";

    public string Action => "exportJson";

    public Task<Result<ReportExportResult>> HandleAsync(
        ExportJsonReportRequest payload,
        CancellationToken cancellationToken) =>
        _reportExportService.ExportJsonAsync(payload.Host, payload.OpenAfterExport, cancellationToken);
}
