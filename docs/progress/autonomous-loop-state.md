# Autonomous Loop State

Maintained by the autonomous development loop. One entry per iteration.

## Current position

- **Milestone:** M5 — Reporting/export — **COMPLETE** (M5 was moved before M4
  by user decision on 2026-07-02; M4 (AD) is deferred and needs its
  access-strategy ADR before it starts)
- **M3:** complete and accepted 2026-07-02
- **User decisions for M5 (2026-07-02):** slice 1 = HTML executive summary of
  latest inventory snapshot + latest persisted security scan only (no
  diagnostics — live-only, never silently triggered by an export); save
  dialog, user picks the path, no silent writes to Documents/AppData; English;
  self-contained HTML, inline CSS, printable, no external assets; slice 2 =
  JSON export of the same data set; no PDF, no report designer.

## M5 slice plan

| Slice | Status | Content |
|---|---|---|
| 1 | ✅ 2026-07-02 | ADR 0004 (cross-module read contracts), report data providers, HTML generator, save-dialog export, Reporting page, 12 tests |
| 2 | ✅ 2026-07-02 | JSON export of the same data set (shared export flow) |
| — | — | **M5 complete ⇒ awaiting user review** |
- **M2:** complete and accepted by user on 2026-07-02 (footer overflow fixed in 84d41c0)
- **User decisions for M3 slice 1 (2026-07-02):** no persistence (UI state only;
  revisit with M5 if reporting needs diagnostics history); DNS probe default
  cloudflare.com via Wec:Diagnostics:DnsProbeHostname (never hardcoded);
  gateway ping: PASS on reply, WARNING on timeout (ICMP often blocked),
  NOT_RUN without gateway, FAIL only on local probe errors/invalid address;
  own DiagnosticResult model + DiagnosticStatus enum, no SecurityFinding reuse.

## M3 slice plan

| Slice | Status | Content |
|---|---|---|
| 1 | ✅ 2026-07-02 | Diagnostics module, network config + gateway + DNS diagnostics, Diagnostics page, 12 tests, no persistence |
| 2 | ✅ 2026-07-02 | Domain/workgroup, time sync, event log summary, service status + IEventLogReader seam, 16 tests |
| — | — | **M3 complete ⇒ awaiting user review before M4** |
- **M1.1:** complete and accepted by user on 2026-07-02
- **User decisions for M2 (2026-07-02):** new module `Wec.Modules.Security` +
  `frontend/src/features/security`; scan history (`security_scans` +
  `security_findings`, minimal schema); severities fixed per check
  (SMB1/firewall-off/Defender-off = HIGH, RDP/SecureBoot-off = MEDIUM,
  TPM = LOW–MEDIUM by evidence, BitLocker-blocked = INFO/LOW per existing
  error semantics, risky local admins = MEDIUM; unclear ⇒ conservative lower
  + documented reasoning); no configurable severity; Security page with scan
  status/button, severity+category filters, evidence, recommendation,
  elevation display. Batch mode: one coherent slice per iteration.

## M2 slice plan

| Slice | Status | Content |
|---|---|---|
| 1 | ✅ 2026-07-02 | Module skeleton, models, scan-history persistence + migration, firewall check, runScan/getLatestScan handlers, Security page with filters, 10 new tests |
| 2 | ✅ 2026-07-02 | All eight remaining checks + IRegistryReader abstraction + 24 tests |
| — | — | **M2 complete ⇒ stopped with NEEDS_USER_REVIEW** |
- **M1 (Local hardware inventory):** complete — 10 steps, commits `34ddc8d`…`0ef3790`,
  DoD verified (unelevated start, CIM → SQLite cache → bridge → React,
  RequiresElevation path, 14 backend tests)

## M1.1 checklist

| Task | Status | Notes |
|---|---|---|
| Vitest tests for the bridge client | ✅ this iteration | 9 tests: correlation, concurrent requests, typed errors, timeout, unavailable, subscribe |
| Loading state | ✅ done in M1 | `HardwareInfoPage` LoadState `loading` |
| Readable error state | ✅ done in M1 | Error card with message |
| Requires-elevation state | ✅ done in M1 | `StatusBadge` variant `elevation` on BitLocker card |
| Refresh inventory button | ✅ done in M1 | forceRefresh via bridge |
| Show app version | ✅ iteration 2 | `system/getAppInfo` + sidebar footer; version from Directory.Build.props (0.1.0) |
| Show database path | ✅ iteration 2 | resolved from DatabaseOptions, shown in footer with tooltip |
| Show log file path | ✅ iteration 2 | resolved from LoggingOptions; footer also shows elevation badge |
| Open logs folder button | ✅ iteration 3 | `system/openLogsFolder`; path only from validated options, never from payload |
| README: run instructions + current limitations | ✅ iteration 4 | "Current limitations" section added |
| Document unelevated behavior + cache verification | ✅ iteration 4 | README sections "Unelevated behavior" and "Verifying the inventory cache" |

## Iteration log

### 2026-07-02 — Iteration 1
- Task: add Vitest tests for `bridgeClient.ts`
- Changes: `frontend/package.json` (vitest+jsdom devDeps, `test` script),
  `frontend/vite.config.ts` (vitest config, jsdom environment),
  `frontend/src/shared/bridge/bridgeClient.test.ts` (9 tests)
- Gates: `npm test` 9/9 ✅ · `npm run build` ✅ · `dotnet build` 0 warnings ✅ ·
  `dotnet test` 14/14 ✅
- Next recommended: system-info surface (app version + database path + log path)
  as one vertical slice — new `system/getAppInfo` bridge action + UI footer/panel

### 2026-07-02 — Iteration 2
- Task: show app version, database path, log path (system-info vertical slice)
- Changes: `GetAppInfoHandler` (system/getAppInfo: version, resolved DB path,
  resolved log dir, isElevated), registered in Host; `Version` 0.1.0 in
  Directory.Build.props; sidebar `AppInfoFooter` with elevation badge;
  api-types mirror; `.claude/` gitignored (agent tooling artifact)
- Gates: `npm test` 9/9 ✅ · `npm run build` ✅ · `dotnet build` ✅ ·
  `dotnet test` 14/14 ✅ · live run: getAppInfo answered success: true ✅
- Next recommended: open-logs-folder button (host-side shell-open of the log
  directory — benign, but document as a host action), or README limitations
  section + unelevated/cache documentation to close out the docs items

### 2026-07-02 — Iteration 3
- Task: open-logs-folder button
- Changes: `OpenLogsFolderHandler` (system/openLogsFolder; shell-opens the
  log directory, path exclusively from LoggingOptions — payload carries no
  path, so the bridge cannot open arbitrary folders), Host registration,
  "Open" button in the sidebar footer
- Gates: `dotnet build` ✅ · `npm test` 9/9 ✅ · `npm run build` ✅ ·
  `dotnet test` 3/3 assemblies ✅
- Next recommended: final docs slice — README "current limitations" section
  plus unelevated-behavior and cache-verification documentation; completing
  it finishes M1.1 ⇒ stop with NEEDS_USER_REVIEW

### 2026-07-02 — Iteration 4
- Task: README limitations + unelevated/cache documentation (final M1.1 item)
- Changes: README sections "Unelevated behavior", "Verifying the inventory
  cache", "Current limitations"
- Gates: docs-only change; dotnet build/test and npm test/build re-run green
- **M1.1 complete ⇒ loop stopped with NEEDS_USER_REVIEW.**
  Open decisions for the user before M2 (see final report): findings
  persistence model, severity mapping ownership, and whether M2 becomes a new
  Wec.Modules.Security module (it should, per architecture) — plus review of
  the M1.1 UX in the running app.

### 2026-07-02 — Iteration 5 (M2 slice 1)
- Task: Security module vertical slice with firewall check
- Key design points: `ISecurityCheck` converts expected failures into INFO
  findings (visible, never silent); crashing check ⇒ scan status
  COMPLETED_WITH_ERRORS, other checks keep running; `MSFT_NetFirewallProfile`
  GpoBoolean (0/1/2) handled, NotConfigured treated as enabled (no false alarm);
  scan history preserved (no replace-on-save)
- **Lesson recorded:** EF Core 9 fails `Migrate()` when the runtime model
  differs from the snapshot (PendingModelChangesWarning). Integration tests
  must compose the model with ALL module assemblies —
  `IntegrationDbContextFactory` is now the single place to register them.
- Gates: dotnet 24/24 ✅ · vitest 9/9 ✅ · builds clean ✅ · migration applied
  on the real DB at startup (421 ms) ✅
- Next: slice 2 — remaining eight checks as one batch with tests

### 2026-07-02 — Iteration 6 (M2 slice 2) — M2 COMPLETE
- Task: remaining eight checks as one batch
- New Core abstraction: `IRegistryReader` (read-only, HKLM only) +
  `WindowsRegistryReader` in Infrastructure — same seam pattern as
  IWmiQueryService, no ADR needed
- Checks and severity decisions (conservative-lower rule where unspecified):
  - Defender: AV disabled = HIGH (fixed); RTP-only off = MEDIUM (weaker state)
  - SMB1 enabled (Win32_OptionalFeature) = HIGH (fixed); absent = no finding
  - RDP enabled (fDenyTSConnections=0) = MEDIUM (fixed); missing value =
    Windows default deny = no finding
  - BitLocker: unelevated = INFO not-run w/ RequiredPrivilege (existing
    semantics); unprotected volume = MEDIUM (range MEDIUM-HIGH unspecified)
  - Secure Boot disabled = MEDIUM (fixed); state missing (legacy BIOS/VM)
    = LOW (range LOW-MEDIUM)
  - TPM: access denied = INFO not-run; absent = LOW (VM-plausible evidence);
    present-but-disabled = MEDIUM (stronger evidence)
  - OS support: offline lifecycle table (Home/Pro dates, builds 19044-26200);
    past EOS = MEDIUM (range MEDIUM-HIGH, dates edition-approximate);
    unknown build = INFO
  - Local Administrators: membership documented as INFO; broad principals
    (EN/DE well-known names) = MEDIUM; group resolved via SID S-1-5-32-544
    (display name is localized)
- Gates: dotnet 48/48 ✅ · vitest 9/9 ✅ · builds clean ✅
- Open user decisions for M3: none technical yet — M3 needs review/kickoff

### 2026-07-02 — Iteration 7 (M3 slice 1)
- Task: network diagnostics vertical slice
- New Core seams: INetworkInfoProvider, IPingProbe, IDnsResolver
  (System.Net-based implementations in Infrastructure/Network — no WMI needed);
  new ErrorCode.NetworkProbeFailed (ADR 0003 + api-types updated)
- Semantics: probe replies distinguish "ran but negative" (Success=false ⇒
  WARNING) from "could not run" (Result failure ⇒ FAIL/NOT_RUN); crashing
  diagnostic becomes a visible FAIL result, run continues
- No persistence, no getLatestRun handler (approved YAGNI)
- Gates: dotnet 60/60 ✅ · vitest 9/9 ✅ · builds clean ✅ · app start clean
  (EF model unchanged with diagnostics assembly registered) ✅
- Note: user's running app instance locked Host output during build; instance
  stopped after compile succeeded, user informed to restart

### 2026-07-02 — Iteration 8 (M3 slice 2) — M3 COMPLETE
- Task: remaining four diagnostics as one batch
- New Core seam: IEventLogReader (System.Diagnostics.Eventing.Reader impl;
  XPath filter Level 1/2 + lookback window; Security log unelevated ⇒
  ACCESS_DENIED ⇒ NOT_RUN with RequiredPrivilege); new
  ErrorCode.EventLogUnavailable (ADR 0003 + api-types updated)
- Status decisions (all read-only, conservative):
  - Domain/workgroup: informational PASS either way, membership in evidence;
    workgroup gets a should-it-be-joined hint
  - Time sync: NoSync or service Disabled = WARNING; Stopped+Manual = PASS
    (trigger start is normal on workgroup machines, documented in test)
  - Event log: criticals > 0 or errors > threshold (option, default 50/24h,
    capped at EventLogMaxEntries) = WARNING with top-3 providers; one result
    per configured log (Wec:Diagnostics:EventLogNames, default ["System"])
  - Services: stopped Auto service or missing service = WARNING (summary
    result with per-service evidence); monitored set is an option
    (default Dhcp, Dnscache, LanmanWorkstation, EventLog)
- Gates: dotnet 76/76 ✅ · vitest 9/9 ✅ · builds clean ✅ · app start clean ✅

### 2026-07-02 — Iteration 9 (M5 slice 1)
- Task: Reporting module with HTML executive summary export
- **ADR 0004 (Proposed):** cross-module read contracts in Wec.Core.Contracts —
  owning module implements (InventoryReportDataProvider,
  SecurityReportDataProvider), consumers depend on the Core interface only;
  enums cross as strings + SeverityRank; deliberate DTO duplication instead of
  module references / domain-in-Core / in-process handler calls
- New Core seams: ISaveFileDialogService (implemented in HOST — WinForms UI,
  not Infrastructure), IShellLauncher (Infrastructure); new
  ErrorCode.FileWriteFailed
- Export flow: dialog cancel = success{cancelled:true} (not an error);
  openAfterExport flag opens only the file just written — no path ever
  crosses the bridge inbound; nothing-to-export = NOT_FOUND before any dialog
- HTML generator is a pure static function: deterministic, HTML-encodes all
  values (XSS test), no external references (test-enforced)
- Gates: dotnet 88/88 ✅ · vitest 9/9 ✅ · builds clean ✅ · app start clean ✅
- Next: M5 slice 2 — JSON export of the same data set

### 2026-07-02 — Iteration 10 (M5 slice 2) — M5 COMPLETE
- Task: JSON export of the same data set
- Shared private export flow (gather → no-data check → dialog → write →
  optional open) reused by HTML and JSON; HtmlExportResult renamed to
  ReportExportResult (used by both handlers)
- JSON = camelCase, indented serialization of ExecutiveSummaryContext
  (machineName, appVersion, generatedAtUtc, inventory, securityScan;
  missing sections rendered as null, not omitted)
- reporting/exportJson handler + second export button in the UI
- Gates: dotnet 91/91 ✅ · vitest 9/9 ✅ · builds clean ✅ · app start clean ✅
- Roadmap state: M1, M1.1, M2, M3, M5 complete; M4 (AD) deferred by user,
  requires access-strategy ADR; then M6/M7/M8/M9 per roadmap order

## Standing constraints (from loop definition)

- One small task per iteration; finish M1.1 before M2.
- Milestone completion ⇒ stop with NEEDS_USER_REVIEW, do not auto-start next.
- Read-only behavior everywhere until M6; ADR before any architectural change.
