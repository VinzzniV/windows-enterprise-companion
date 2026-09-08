# Audit measures — implementation evidence

Updated: 2026-09-08.

This document records implementation status and technical reassessments for
M01-M16. The blind UX audit remains unchanged. A measure is marked complete
only after its listed acceptance criteria have targeted automated evidence and
the applicable milestone checks are green.

| Measure | Status | Evidence |
| --- | --- | --- |
| M01 | Complete | Backend policy, paging and Action Center projection tests cover known Nessus Critical/High evidence under partial coverage, suppression of unproven Missing findings, simultaneous problem/incomplete classification and distinct work-item/device/instance units. Clients and Action Center component tests cover the visible qualified counts, timestamps and coverage warning. |
| M02 | Complete | Cache/handler tests prove forced reload and monotonic in-process snapshot revision. Environment tests cover forced backend payloads, pending invalidation, cancellation generations and stale-response rejection. Page tests cover forced refresh and replacement of selected Action Center details with the current item. |
| M03 | Complete | ADR 0021 defines complete host/address identities, exact-first resolution and proven unique aliases. Core, correlation, provider and frontend tests cover distinct IPs, equal short names in different domains, foreign same-name targets, unique legacy aliases and ambiguous links. |
| M04 | Complete | A non-destructive migration adds nullable normalized identity keys and a filtered unique index. Real-SQLite tests cover newest-capture-wins concurrency, failure injection, retained legacy duplicates and atomic replacement. |
| M05 | Complete | Inventory, Security and Health use explicit loading, missing, stored-read failure and live-run failure states. The same UI matrix covers timeout, unavailable bridge, database failure and unreadable payload; stored reloads remain read-only and failed live refreshes retain the previous result. |
| M06 | Complete | Comparison blocks absence claims when either software capture is unavailable, separates product presence from version differences, normalizes GPU order/whitespace, applies documented byte tolerances to raw RAM/disk totals and permanently displays Inventory/Security capture times and coverage per client. |
| M07-M11 | Planned | Not yet accepted. |
| M12 | Complete | Real-SQLite integration coverage proves that blank legacy hosts are excluded from list results while both persisted rows remain unchanged. |
| M13-M16 | Planned | Not yet accepted. |

## M01/M02 technical reassessment

- Positive source evidence and source completeness are independent dimensions.
  Partial or truncated Nessus results may establish known findings but never
  establish absence. Global incomplete coverage therefore coexists with a
  device's problem state and is exposed separately by summary and filter.
- `SnapshotRevision` identifies one successful environment load inside the
  running process. Reused cache results keep the revision; every successful
  reload receives the next revision. The assessment timestamp remains the
  durable comparison point across application restarts, so no persistence or
  new migration is warranted.
- Settings changes invalidate the current generation and require the next
  dependent page load to force the backend cache. A successful forced page load
  clears the stale shared full-result view without causing a second refresh.
  Navigation alone still performs only bounded management-data reads and never
  starts a per-device Inventory, Health or Security scan.

## M01/M02 verification

- Release build: passed with zero warnings and zero errors.
- Backend: 748 tests passed.
- Frontend: 470 tests passed; production build passed.
- Bridge contract check: 390 generated types current.
- Dependency audit: zero high-severity findings and zero findings overall.
- Release host: local assets and WebView2 initialized; startup bridge requests
  completed successfully. Native-window automation was unavailable in this
  session, so visual interaction remains a later focused smoke gate rather than
  a repeated blind audit.

## M12 technical reassessment and verification

Invalid blank-host rows are a storage-maintenance concern, not part of a read
contract. `ListHostsAsync` now uses a no-tracking filtered query and performs no
write. The seven real-SQLite hardware snapshot tests pass, including the revised
non-mutation regression.

## M03/M04 technical reassessment

- ADR 0021 makes the complete normalized hostname, FQDN or IP address the
  primary identity. A short name is accepted only as a source-proven,
  unambiguous alias. Exact matches always win. Local detection is limited to
  the exact machine name and the locally derived FQDN, so an equal first DNS
  label in another domain remains remote.
- Existing inventory was inspected read-only before designing the migration:
  41 rows represented 41 distinct normalized identities; no blank, duplicate
  or ambiguous short-name group was present. No host value is recorded here.
- The migration was also applied to an isolated copy of that database. All 41
  rows remained, all 41 unique keys were backfilled and no duplicate current
  identity was created. The live database was not changed during verification.
- Existing ambiguous duplicate rows are deliberately retained with a null
  transition key. New writes use one SQLite upsert guarded by capture time;
  this avoids a delete/insert gap and prevents a late older scan from replacing
  newer evidence.
- Client routes and the recent-comparison cache use exact identities first.
  Version 2 of the local recent-comparison view intentionally discards the old
  universal short-name normalization; a proven unique legacy short route still
  resolves, while an ambiguous one does not.

## M03/M04 verification

- Release build: passed with zero warnings and zero errors.
- Backend: all 764 tests passed, including 149 real-SQLite integration tests.
- Frontend: all 475 tests passed; production build passed.
- Bridge contract check: 390 generated types current.
- Migration rehearsal: isolated existing-database copy retained 41 of 41 rows
  and produced 41 unique non-null identity keys.
- `git diff --check`: passed.

## M05 technical reassessment and verification

- A missing stored record is a successful nullable read for Security and Health,
  and the existing typed `NOT_FOUND` result for Inventory. Transport, timeout
  and database failures never enter the empty state.
- Corrupt Inventory and Health JSON is no longer discarded as a cache miss. It
  maps to `STORED_DATA_UNREADABLE`, preserving the record and exposing an
  actionable read failure. Missing optional fields remain compatible and are
  still interpreted through the existing partial-coverage contracts.
- Reload controls call only the stored read actions. Live Inventory, Security
  and Health failures keep an already loaded saved result visible with an
  explicit failure notice.
- 24 focused frontend section tests, 79 focused module tests and 16 real-SQLite
  persistence tests passed. The Release solution build and production frontend
  build passed; 390 generated bridge contract types are current.

## M06 technical reassessment and verification

- Installed software is compared only when both capture lists are present.
  Unknown or failed capture on either side suppresses all one-sided absence
  claims. Matching product names remain shared while reported version changes
  are listed separately.
- Hardware display values remain visible, while equality uses normalized text
  and raw numeric totals. GPU names ignore surrounding/repeated whitespace and
  device order. Memory is equivalent within 1% or 64 MB; total storage within
  1% or 1 GB. The applied rule is visible beside the property.
- The result keeps an evidence card for both clients with Inventory and Security
  capture times, software coverage and Security completeness.
- 22 focused comparison tests passed and the production frontend build passed.
