# Wec.Modules.Reporting

Exports an executive summary of **one machine** — the local machine, or a
scanned remote client selected by `host`: the latest inventory snapshot and
the latest persisted security scan for that host. No live scans are triggered
by an export; what was captured is what gets reported.

## Bridge actions

`host` is optional on every action: omitted/`null` targets the local machine;
a host string targets the already-scanned remote client with that cache key.

| Action | Payload | Result |
|---|---|---|
| `reporting/getOverview` | `{ host?: string \| null }` | `ReportOverview` — available data plus Security coverage/readiness |
| `reporting/exportHtml` | `{ openAfterExport?: boolean, host?: string \| null }` | `ReportExportResult` — save-dialog flow, self-contained HTML |
| `reporting/exportJson` | `{ openAfterExport?: boolean, host?: string \| null }` | `ReportExportResult` — same data set as camelCase JSON |

## Design notes

- Data comes exclusively through the ADR 0004 cross-module read contracts
  (`IInventoryReportDataProvider`, `ISecurityReportDataProvider`) — this
  module references no other module.
- With per-host caches/scans (ADR 0007), the providers key on the target's
  `ScanTarget.CacheKey` (`host` null ⇒ local). A single report is always one
  host; a multi-host *aggregate* report is still a future slice.
- Security report data contains the persisted per-check outcomes and derived
  coverage. The overview, HTML and JSON distinguish complete, incomplete and
  unavailable legacy coverage. Failed/elevation-blocked checks remain visible.
- An empty finding list is labelled `PASS` only when coverage is complete. With
  incomplete or unknown coverage it means only that no findings were observed;
  it is explicitly not a complete assessment.
- Scans created before coverage version 1 remain coverage-unknown after the
  database migration. Reporting never reconstructs or guesses missing outcomes.
- The overview and both export formats include a readiness block for the two
  persisted sources. Each source is `READY`, `MISSING`, `STALE`, or
  `INCOMPLETE`, with capture time, age, and provenance. Readiness is advisory:
  a partial export remains possible, but the report records why it must not be
  treated as current or complete.
- The default freshness window is 24 hours for Inventory and Security. Override
  `Wec:Reporting:MaximumInventoryAge` or
  `Wec:Reporting:MaximumSecurityScanAge` with a positive `TimeSpan` when the
  operating procedure requires a different window.
- Dialog cancel is a success (`cancelled: true`), not an error. Nothing to
  export ⇒ `NOT_FOUND` before any dialog. `openAfterExport` opens only the
  file just written — no path ever crosses the bridge inbound.
- The HTML generator is a pure static function: deterministic, HTML-encodes
  every value, no external references (both test-enforced).

## Tests

`tests/Wec.Modules.Reporting.Tests` — export flow with mocked providers,
dialog service and file writer; complete/incomplete/legacy Security coverage;
XSS/encoding and no-external-assets tests.
