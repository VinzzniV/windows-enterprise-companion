using Wec.Core.Contracts;

namespace Wec.Modules.Reporting.Application;

public sealed record ExecutiveSummaryContext(
    string MachineName,
    string AppVersion,
    DateTimeOffset GeneratedAtUtc,
    InventoryReportData? Inventory,
    SecurityReportData? SecurityScan);
