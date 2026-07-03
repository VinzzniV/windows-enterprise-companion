# Wec.Modules.Reporting

Exports an executive summary of the **local machine**: the latest inventory
snapshot and the latest persisted local security scan. No live scans are
triggered by an export; what was captured is what gets reported.

## Bridge actions

| Action | Payload | Result |
|---|---|---|
| `reporting/getOverview` | `{}` | `ReportOverview` — what data is available for export |
| `reporting/exportHtml` | `{ openAfterExport?: boolean }` | `ReportExportResult` — save-dialog flow, self-contained HTML |
| `reporting/exportJson` | `{ openAfterExport?: boolean }` | `ReportExportResult` — same data set as camelCase JSON |

## Design notes

- Data comes exclusively through the ADR 0004 cross-module read contracts
  (`IInventoryReportDataProvider`, `ISecurityReportDataProvider`) — this
  module references no other module.
- With per-host caches/scans (ADR 0007), the providers deliberately return
  the **local machine's** data; a multi-host report is a future slice.
- Dialog cancel is a success (`cancelled: true`), not an error. Nothing to
  export ⇒ `NOT_FOUND` before any dialog. `openAfterExport` opens only the
  file just written — no path ever crosses the bridge inbound.
- The HTML generator is a pure static function: deterministic, HTML-encodes
  every value, no external references (both test-enforced).

## Tests

`tests/Wec.Modules.Reporting.Tests` — export flow with mocked providers,
dialog service and file writer; XSS/encoding and no-external-assets tests.
