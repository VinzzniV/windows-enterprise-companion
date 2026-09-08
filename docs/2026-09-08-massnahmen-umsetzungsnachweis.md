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
| M07 | Implemented; WebView smoke pending | `DataTable` keeps native row semantics, activates a focused row with Enter/Space, exposes selection with `aria-selected`, shows a focus outline and ignores bubbled events from inputs, labels, buttons, links and other interactive descendants. Shared and Clients-page tests prove that Space changes exactly the checkbox without navigation; all interactive table callers were inventoried and their affected tests pass. Native-window automation is unavailable in this session, so the required real-WebView keyboard check remains an external acceptance gate. |
| M08 | Implemented; layout smoke pending | Cleanup, Error log and Action Center use the same viewport-bound detail dialog with Escape, focus containment and focus return. A 500-row Error-log regression proves that the first selection opens without relying on the page end. Client Event Logs expose an explicit full-message action with host, time, source, safe wrapped text, copy feedback and a warning for the existing 500-character truncation convention. The required real-browser/WebView layout check remains external because this session cannot automate the native window or inject the host bridge into its isolated browser. |
| M09 | Implemented; layout smoke pending | Clients keeps only compact source state, snapshot time and coverage guidance above search. Eleven posture/filter metrics are collapsed by default, and the batch workbench is absent until a client is selected. Running progress stays visible when batch options collapse. The fixed client table orders Device and Overall before explicitly sized provider columns, while semantic badges prohibit mid-word wrapping. The physical 1026 x 671 WebView and higher-zoom check remains external. |
| M10 | Complete | The Patch Management contract and UI call the sum outdated product installations; one client with two outdated products remains two installations. Dashboard labels stored hosts as stored, Clients separates posture assessment from filtered merged candidates, and Compare explicitly identifies its enabled-AD/stored/saved picker population and exclusions. |
| M11 | Complete | Search, posture, source, grouping, page, page size and sort are URL-backed and directly linkable. Detail tabs preserve the exact return URL; button return and browser Back restore the inner app scroll container once. Primary navigation retains the last list URL only for the same directory/management context, Reset clears it, and text typing is debounced to one server read per burst. |
| M12 | Complete | Real-SQLite integration coverage proves that blank legacy hosts are excluded from list results while both persisted rows remain unchanged. |
| M13 | Complete | Local log level filtering precedes the result limit. Configured file-count, byte and continuation-line bounds are returned and displayed with exact coverage and truncation metadata. Remote Event Log results expose their query window, result limit and explicit per-message truncation. The hide action remains a local visibility marker and never claims deletion. |
| M14 | Complete | Client actions distinguish live read-only access, locally replaced scan evidence and locally saved target metadata. Save/unsave failures remain visible and duplicate in-flight target mutations are suppressed; no password enters `SaveTarget`. Network Scan previews its target, exact configured TCP ports, DHCP/account context, non-persistence and no-write behavior, and rejects duplicate starts. The Print port-removal action retains its explicit external-effect text and confirmation. |
| M15 | Complete | Trend x-positions derive from UTC dates, axes name date and finding units, and a keyboard-accessible table exposes every daily severity/asset value without color or hover. Daily totals explicitly cover all assets, while the verdict evidence names start/end dates, common cohort, added/removed assets, common-cohort start/end values and the first deciding severity under the unchanged Critical-to-Low policy. Empty and one-point states remain explicit. |
| M16 | Planned | Not yet accepted. |

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

## M07 technical reassessment and verification

- A clickable table row remains a native `row`; it is focusable and uses
  `aria-selected` for the active state instead of replacing its role with
  `button`. Enter and Space activate only when focus is on the row itself.
- Click and key events from checkboxes, labels, buttons, links, selects,
  textareas, summaries and equivalent ARIA controls do not activate the row.
  This applies centrally to Clients, Users, AD identity results, Nessus
  findings, Error log and Winget-managed products.
- Shared component tests cover row activation, focus styling and child controls;
  the Clients-page regression proves Space selects exactly one client and keeps
  the route unchanged. Scoped caller suites and the production build pass.
- The Release host can be started, but this session exposes no native window to
  automation. Keyboard order and visible focus in the actual WebView remain a
  focused external smoke gate; the blind audit was not changed.

## M08 technical reassessment and verification

- Appending details below 25 or 500 rows cannot meet the visibility contract.
  The shared `DetailDialog` is therefore viewport-bound, independently
  scrollable and sized against dynamic viewport height. It uses the existing
  modal interaction approach from the shell rather than adding a layout
  framework. Escape, backdrop close, contained Tab navigation and focus return
  are central behavior.
- Cleanup keeps its review checkboxes, connectivity evidence, decision and
  reason in the same page-owned session state. Changing the selected canonical
  subject still resets those values through the existing subject-key boundary.
  Error log and Action Center retain their existing detail content but no
  longer place it after the table.
- Client Event Logs now use an explicit `View full message` control. The detail
  surface keeps host, event time, source, level and event code visible, renders
  the provider message as escaped text with preserved wrapping, and offers
  clipboard feedback. The backend now supplies an explicit per-message
  truncation flag, so the UI does not infer completeness from message text.
- All 508 frontend tests pass, including focused dialog, table, 500-row Error
  log, Cleanup, Action Center and Event Log regressions. The TypeScript and
  production Vite build and `git diff --check` pass.

## M10 technical reassessment and verification

- The existing opsi aggregate counts client-product states, not distinct
  clients. Its backend and generated frontend contract now use
  `OutdatedInstallationCount`; the arithmetic is deliberately unchanged.
- A backend fixture proves that one device behind on two products yields two
  outdated installations while the dashboard client population remains one.
  The product table and summary use the same unit and explain the optional
  depot filter.
- The Dashboard Inventory tile says `stored hosts` and exposes its stored
  source, freshness window and capture time. A 41-host regression prevents
  those snapshots from being presented as the complete fleet.
- Clients distinguishes the unfiltered cross-management posture population
  from the filtered table merged with stored Inventory and saved targets.
  Compare explicitly documents its different picker basis: enabled AD
  computers plus stored Inventory/Security hosts and saved client targets.
- 37 Patch Management backend tests and 47 focused frontend tests passed. The
  Release solution and production frontend builds passed with zero compiler
  errors, and all 390 generated bridge contracts are current.

## M11 technical reassessment and verification

- The Clients URL is the durable view contract. It holds text, posture, source,
  grouping, page, page size, sort column and direction; default values remain
  omitted so `/clients` is the explicit reset state and every non-default view
  remains directly linkable.
- Connectivity evidence, batch selection and scroll position remain transient
  navigation state because they are session observations, not shareable query
  criteria. Switching Client 360 tabs preserves that state and the exact list
  return URL.
- Scroll restoration targets the shell's real `overflow-y-auto` container and
  runs once after the restored result is available. Browser Back and the
  visible All clients action both retain the complete URL.
- Primary navigation stores only the last list URL for the current AD/Kaspersky
  identity context. A directory, server or account-context change falls back
  to `/clients` instead of applying a foreign filter silently. No password is
  stored or used in that scope key.
- Search text updates the URL immediately but batches a typing burst into one
  backend read after 250 ms. Other finite filters apply immediately and all
  changes reset paging as appropriate.
- 47 focused App-shell, Clients, Client-detail, Dashboard and scope tests pass;
  all 519 frontend tests and the production TypeScript/Vite build pass.
- A local real-browser run at 1026 × 671 was prepared, but the isolated browser
  cannot receive the WebView host bridge fixture and native-window automation
  is unavailable. Actual visual fit, focus-ring visibility and scrolling in
  WebView2 remain the documented external M08 acceptance gate; the blind audit
  remains unchanged.

## M09 technical reassessment and verification

- The prior default view placed eleven posture tiles and an unused batch card
  before the working table. Clients now keeps the current source states,
  snapshot timestamp and incomplete-coverage warning in one compact context.
  The complete metrics and their existing filter actions remain available in
  one disclosure, so no assessment metadata or drill-down is removed.
- The batch workbench mounts only after explicit row selection. Its operation
  and host options collapse when a batch starts, while selected count, Cancel
  and per-host progress remain visible. Selection alone still invokes no scan.
- Client columns now have feature-owned widths and a fixed, horizontally
  scrollable table. Select, Device and Overall come first; AD, Kaspersky, opsi
  and Nessus follow. Device description and all existing last-seen evidence
  remain present in a denser row. Shared `DataTable` only exposes the opt-in
  column class; it applies no new global compact behavior.
- The common Badge explicitly resets inherited arbitrary word wrapping, so
  labels such as `Cleanup candidate` stay whole. Tests cover the class on all
  canonical semantic states.
- All 509 frontend tests and the production TypeScript/Vite build pass. The
  focused Clients and batch tests prove default workbench absence, collapsed
  posture metrics, column order, explicit selection and progress visibility
  outside collapsed options.
- Native-window automation remains unavailable, and the isolated local browser
  cannot receive the WebView host bridge fixture. The exact 1026 x 671 visual
  fit, higher zoom, sticky header and horizontal scrollbar remain the external
  M09 acceptance gate rather than being claimed from DOM tests.

## M13 technical reassessment and verification

- The `Errors` selection is part of the Host request and is applied while
  parsing, before the result cap. A regression with one older error followed by
  500 warnings proves that the relevant error remains available.
- Local reads are bounded independently of the returned row count: the newest
  configured number of retained files is considered, and only the configured
  tail byte window of each selected file is read. The response distinguishes
  file-selection, byte-window, result-count and continuation-detail truncation;
  the UI states each active boundary instead of claiming complete history.
- More than the configured 40 continuation lines sets an explicit flag on the
  affected entry. The detail dialog displays that flag. `Hide previous entries`
  is described and implemented as a local timestamp visibility marker; log
  files remain untouched.
- Remote Event Log results now include their exact UTC start/end timestamps,
  result limit and an explicit message-truncation bit. The existing severity
  policy and 500-character provider-message limit are unchanged.
- 76 Host tests, 32 Diagnostics tests and 8 focused frontend tests passed. The
  Release solution build, generated-contract check (392 types), production
  frontend build and `git diff --check` passed.

## M14 technical reassessment and verification

- Saving a client persists only host, label, role and an optional username in
  the local WEC database. The session password is not part of `SaveTargetInput`
  or the bridge payload. Unsave removes only that local shortcut.
- Client target save/delete now await their result, keep the current UI state
  on failure, expose the typed error presentation and use an in-flight guard.
  A double-click regression proves that one save request is sent and the
  inspected bridge payload contains no password.
- Inventory and Security state their live read scope and local replacement
  behavior before the action. Event Log names the host/account context and
  remains live and unpersisted. The existing Print unused-port workflow already
  names the signed-in admin, permanent external server deletion, server-side
  refusal boundary and explicit confirmation, so no additional modal was added.
- Network Scan reads its configurable TCP-port list through a small read-only
  policy action. Its live preview names the current range/IP, exact ports or
  disabled port probes, optional DHCP server/account context, active traffic,
  non-persistence and absence of target configuration changes. An in-flight
  guard prevents duplicate scan starts.
- 12 Network Scan backend tests and 39 focused frontend tests passed. The
  Release solution build, generated-contract check (394 types), production
  frontend build and `git diff --check` passed.

## M15 technical reassessment and verification

- Existing daily `TrendPoint` values sum all assets captured on each UTC day;
  changing those values to the intersection would silently change the chart's
  established meaning. They remain all-asset totals and are labelled as such.
  Horizontal positions now derive from parsed UTC dates, so a one-day gap uses
  one quarter of the width of a four-day span.
- The verdict continues to compare only assets present on both the first and
  last date. The response now exposes both dates, common-cohort severity totals
  and the first differing severity under the existing Critical → High → Medium
  → Low order. A regression proves that Critical 1 → 2 yields `Worse` even when
  High falls from 9 → 1.
- The visible verdict explanation distinguishes the common cohort from daily
  all-asset totals and names added/removed counts. The chart supplies a UTC date
  axis, numeric findings scale and per-point titles; an expandable native table
  exposes all daily values without relying on color or pointer hover. The
  one-point state exposes its first snapshot values and the earliest possible
  second date; the empty state remains explicit.
- Dashboard coverage now qualifies the trend as a common-asset comparison and
  names its start/end dates.
- 18 Vulnerability Management backend tests and 29 focused Vulnerabilities/
  Dashboard frontend tests passed. The Release solution build,
  generated-contract check (395 types), production frontend build and
  `git diff --check` passed.
