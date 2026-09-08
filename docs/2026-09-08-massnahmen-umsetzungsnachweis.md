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
| M03-M11 | Planned | Not yet accepted. |
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
