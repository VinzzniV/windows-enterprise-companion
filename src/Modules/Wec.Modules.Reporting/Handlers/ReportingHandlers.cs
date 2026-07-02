using Wec.Core.Messaging;
using Wec.Core.Results;
using Wec.Modules.Reporting.Application;

namespace Wec.Modules.Reporting.Handlers;

public sealed record GetReportOverviewRequest;

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
        _reportExportService.GetOverviewAsync(cancellationToken);
}

public sealed record ExportHtmlReportRequest(bool OpenAfterExport = false);

internal sealed class ExportHtmlReportHandler : IActionHandler<ExportHtmlReportRequest, HtmlExportResult>
{
    private readonly ReportExportService _reportExportService;

    public ExportHtmlReportHandler(ReportExportService reportExportService)
    {
        _reportExportService = reportExportService;
    }

    public string Module => "reporting";

    public string Action => "exportHtml";

    public Task<Result<HtmlExportResult>> HandleAsync(
        ExportHtmlReportRequest payload,
        CancellationToken cancellationToken) =>
        _reportExportService.ExportHtmlAsync(payload.OpenAfterExport, cancellationToken);
}
