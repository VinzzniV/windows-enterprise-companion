# Autonomous Loop State

Maintained by the autonomous development loop. One entry per iteration.

## Current position

- **Milestone:** M4 — COMPLETE 2026-07-02 (both slices) ⇒ awaiting user
  review; remaining roadmap: M6 (remediation, needs ADR), M7 (AI, optional)
- ADR 0006 governs all AD access; ADR 0004 still Proposed (user sign-off
  pending since M5)
- **User decisions for M8 (2026-07-02):** upgrade to .NET 10 LTS first
  (slice 0); package **self-contained win-x64** (~180 MB measured vs 39 MB
  framework-dependent — zero prerequisites on target machines won);
  distribution = portable **ZIP from CI** (slice 1) + **Inno Setup per-user
  installer** to `%LOCALAPPDATA%\Programs` without admin rights (slice 2).
  Explicitly out: MSIX (needs a code-signing certificate), code signing
  (documented SmartScreen limitation), auto-update, trimming (WinForms/EF
  reflection). WebView2: Evergreen assumed, startup detection already exists.
  ADR 0005 documents the direction (written with slice 1).
- **M9:** CI half done; release half unblocks after M8 slice 1/2
- **M5:** complete 2026-07-02; **M4 (AD)** deferred by user, needs its
  access-strategy ADR before it starts
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

### 2026-07-02 — Iteration 11 (M9 slice 1)
- Task: CI workflow (user pulled M9 forward)
- .github/workflows/ci.yml: windows-latest (net9.0-windows host), Node 22 +
  npm cache, .NET pinned via global.json; gates in order: npm ci → npm test →
  npm run build (fills wwwroot) → dotnet build Release → dotnet test Release;
  publish + artifact upload ONLY on green master pushes (14-day retention)
- Locally replayed the exact pipeline: Release build 0 warnings, 91/91 tests
  green in Release, publish output verified (exe + appsettings + wwwroot)
- Remote created 2026-07-02: private repo
  https://github.com/VinzzniV/windows-enterprise-companion (gh CLI installed
  via winget, user authenticated as VinzzniV); first CI run green in 2m21s,
  artifact `wec-host-<sha>` uploaded. Runner annotation: v4 actions are
  Node-20-based (deprecated) — bump action majors when available
- M9 release half (tags/installer) blocked on M8 packaging direction

### 2026-07-02 — Iteration 12 (M8 slice 0) — .NET 10 LTS upgrade
- Why first: .NET 9 is STS and left support 2026-05-12; baking an EOL runtime
  into a self-contained package would be day-one debt
- Changes: global.json → SDK 10.0.301 (installed via winget); all 13 csproj
  TFMs net9.0→net10.0; CPM M.E.*/EF/EventLog 9.0.13→10.0.9; dotnet-ef tool
  10.0.9; docs updated (README, CLAUDE.md, architecture doc; ADR 0001 left
  as historical record)
- **Lesson: NuGet Audit + TreatWarningsAsErrors** — the .NET 10 SDK audits
  transitive packages; SQLitePCLRaw.lib.e_sqlite3 2.1.11 (via EF Sqlite) has
  CVE-2025-6965 with no 2.1.x patch. Fixed by explicitly referencing
  SQLitePCLRaw.bundle_e_sqlite3 3.0.3 (SQLite ≥ 3.50.2); integration tests
  against real SQLite files prove compatibility. Never suppress NU1903.
- **Lesson: CA1873** (new .NET 10 analyzer) — LogDebug/LogInformation calls
  whose arguments box value types or call members are errors now. Fixed the
  11 flagged sites with `[LoggerMessage]` source-generator partial methods
  (built-in level guard, zero alloc); warning/error-level calls are exempt.
  Watch out: the logger argument itself must be a cheap expression (no
  `GetRequiredService` inline).
- Gates: dotnet build 0 warnings ✅ · dotnet test 91/91 (net10.0) ✅ ·
  vitest 9/9 ✅ · npm build ✅

### 2026-07-02 — Iteration 13 (M8 slice 1)
- Task: ADR 0005 + self-contained ZIP packaging in CI
- ADR 0005 (Accepted — decisions made interactively by the user): runtime
  self-contained win-x64; formats ZIP (CI) + Inno per-user (slice 2); no
  signing/MSIX/auto-update/trimming/MSI, each with documented reasoning
- ci.yml: publish step now `--runtime win-x64 --self-contained true`
  (compiles for the RID — no `--no-build`); versioned
  `wec-<version>-win-x64.zip` via Compress-Archive (version parsed from
  Directory.Build.props); artifact renamed `wec-win-x64-<sha>`
- Locally replayed: publish OK, ZIP 76.9 MB (≈180 MB uncompressed), exe +
  wwwroot verified; smoke test: published exe ran 10 s, WebView2 up, bridge
  answered getHardwareInfo, BitLocker correctly RequiresElevation, process
  stopped cleanly
- README: CI section + limitations (SmartScreen note instead of "no
  packaging yet"); .gitignore: publish/ + local ZIPs
- Gates: dotnet build 0 warnings ✅ · dotnet test 91/91 ✅ · vitest 9/9 ✅ ·
  npm build ✅

### 2026-07-02 — Iteration 14 (M8 slice 2) — M8 COMPLETE
- Task: Inno Setup per-user installer
- packaging/wec-installer.iss: PrivilegesRequired=lowest, DefaultDirName
  {userpf}\Wec (= %LOCALAPPDATA%\Programs\Wec), Start menu entry,
  uninstaller; AppVersion injected via /DAppVersion (CI reads
  Directory.Build.props); uninstall deliberately preserves
  %LOCALAPPDATA%\Wec runtime data
- ci.yml: installer built with the preinstalled Inno Setup 6 on
  windows-latest; second artifact wec-setup-<sha> next to the ZIP
- Locally replayed full cycle: ISCC build (53.8 MB setup.exe), silent
  per-user install verified (exe, wwwroot, shortcut, uninstaller), app ran
  from the install dir, silent uninstall removed program + shortcut and
  preserved wec.db
- README: new Installation section (both artifacts, SmartScreen note)
- Gates: dotnet build 0 warnings ✅ · dotnet test 91/91 ✅ · vitest 9/9 ✅ ·
  npm build ✅
- **M8 complete ⇒ M9 release half (tags + release attaching both
  artifacts) is now unblocked**

### 2026-07-02 — Iteration 15 (M9 slice 2) — M9 COMPLETE
- Task: release process (tags → GitHub release with both artifacts)
- Deliberately NO second workflow: ci.yml gets a `v*` tag trigger so a
  release runs through the identical gates — no build-step duplication that
  could drift. `permissions: contents: write` added for `gh release create`.
- Fail-fast guard right after checkout: tag name must equal
  `v<Version from Directory.Build.props>`, otherwise the run fails before
  building anything
- Release step (tags only): `gh release create <tag> <zip> <setup.exe>
  --generate-notes --verify-tag` (pwsh does not glob for native commands —
  setup path resolved via Get-ChildItem)
- Release procedure documented in README (bump version → commit → tag → push)
- Validated end to end: tag v0.1.0 pushed → tag run green → release
  **v0.1.0 published** with wec-0.1.0-win-x64.zip (80.7 MB) and
  wec-0.1.0-setup.exe (56.4 MB); parallel master run also green
- **M9 complete.** Remaining roadmap: M4 (AD, needs access-strategy ADR),
  M6 (remediation, needs ADR), M7 (AI, optional)

### 2026-07-02 — Iteration 16 (M4 kickoff)
- Task: ADR 0006 — Active Directory access strategy (required before any
  M4 implementation)
- Decision: LDAP via System.DirectoryServices.Protocols behind a new Core
  seam `IDirectoryReader` (search-only shape = read-only enforced by the
  interface, IWmiQueryService pattern); current Windows identity only, no
  credential storage; domain detection via Win32_ComputerSystem; new
  ErrorCode.DirectoryUnavailable; paged searches with attribute allowlists;
  all tunables as Wec:ActiveDirectory options
- Rejected: ADSI (COM + read/write API mix), AccountManagement (legacy,
  slow), PowerShell RSAT module (dependency weight)
- Gates: docs-only; build/test baseline green from v0.1.0 release runs
- Next: M4 slice 1 — Wec.Modules.ActiveDirectory + IDirectoryReader seam +
  domain detection + DC discovery + user/group overview + UI + tests

### 2026-07-02 — Iteration 17 (M4 slice 1)
- Task: AD module vertical slice (overview)
- New Core seam per ADR 0006: IDirectoryReader (search-only) +
  DirectorySearchQuery/DirectoryEntryData (case-insensitive multi-value
  attributes, GetLong for AD numerics); ErrorCode.DirectoryUnavailable
  (ADR 0003 + api-types updated)
- Infrastructure: LdapDirectoryReader (S.DS.Protocols 10.0.9, Negotiate,
  paged via PageResultRequestControl, no referral chasing, empty attribute
  list ⇒ RFC 4511 "1.1" = count-only); InsufficientAccessRights ⇒
  ACCESS_DENIED, other LDAP failures ⇒ DIRECTORY_UNAVAILABLE
- Module: DirectoryOverviewService — WMI domain detection first (workgroup ⇒
  valid domainJoined:false result, directory never touched — test-enforced);
  RootDSE ⇒ defaultNamingContext; DC discovery via userAccountControl bit
  8192; user/disabled/group/computer counts; activedirectory/getOverview
- UI: Active Directory page (analyze button, workgroup card, stat tiles,
  DC list); nav + api-types extended
- No persistence, no factory changes needed (module has no EF entities —
  like Diagnostics/Reporting)
- Gates: dotnet build 0 warnings ✅ · dotnet test 100/100 (7 assemblies) ✅ ·
  vitest 9/9 ✅ · npm build ✅ · app start smoke test with module registered ✅
- Next: M4 slice 2 — hygiene checks (inactive users/computers,
  password-never-expires, privileged groups, disabled-but-privileged)

### 2026-07-02 — Iteration 18 (M4 slice 2) — M4 COMPLETE
- Task: hygiene checks batch (activedirectory/getHygiene)
- Seam extension: binary attribute values (objectSid) cross as Base64;
  DirectoryEntryData.GetBytes decodes — needed because privileged groups are
  resolved by well-known SID (domain SID from domain head + RIDs 512/519/518,
  Builtin S-1-5-32-544), which survives localized group names
- DomainContextService extracted (shared WMI detection + RootDSE step for
  overview and hygiene); AdFilters centralizes LDAP filters incl. RFC 4515
  escaping (tested with parens/star/backslash DNs)
- Rules (exact counts, examples bounded by ExampleLimit option): inactive
  users/computers (lastLogonTimestamp < now − InactivityThreshold, enabled
  only), enabled password-never-expires, disabled-but-privileged (direct
  memberOf on privileged group DNs). Documented limits: lastLogonTimestamp
  replication slack, never-logged-on not matched, direct members only,
  no ranged retrieval >1500
- **Lesson: MS.DI requires PUBLIC constructors** — internal ctor on a
  registered service crashes at startup (unit tests pass via
  InternalsVisibleTo!); the app-start smoke test caught it. Convention now:
  internal class + public ctor.
- Gates: dotnet build 0 warnings ✅ · dotnet test 113/113 ✅ · vitest 9/9 ✅ ·
  npm build ✅ · app start smoke test ✅ (after the DI fix)

### 2026-07-02 — Iteration 19 (user-directed UX polish)
- Task: intro animation + general usability (user request, outside milestones)
- SplashIntro: once per app start, ~1.9 s, logo stroke-draw + staggered bars +
  wordmark + sweep; purely decorative overlay (app loads underneath);
  prefers-reduced-motion collapses it to a short static frame
- LogoMark shared by splash and sidebar header; nav got icons + sky accent
  indicator; subtle page-enter transition keyed on route
- Native: MainWindow BackColor + WebView2 DefaultBackgroundColor = slate-950
  (#020617) — kills the white flash before first paint
- Usability: shared Spinner replaces bare "Loading …" texts on all five
  pages; global :focus-visible ring (keyboard nav); dark thin scrollbars;
  selection color
- **Bug found via browser preview: bridgeClient.invoke threw synchronously
  when the bridge is missing — inside a React effect that unmounts the whole
  tree (blank app) instead of reaching .catch(). invoke now always returns a
  rejected promise; test updated accordingly.**
- Gates: vitest 9/9 ✅ · npm build ✅ · dotnet build 0 warnings ✅ ·
  dotnet test 113/113 ✅ · app smoke test with splash in WebView2 ✅

### 2026-07-03 — Remote analysis (goal-directed session, six slices)
- ADR 0007 (remote execution/credentials: WSMan CimSession, in-memory-only
  credentials, remote error taxonomy) + ADR 0006 revised (LDAP credentials,
  domain/DC override, diagnostic error mapping)
- Slice 1: Wec.Core.Targets (ScanTarget/ScanCredentials/ConnectionOptions/
  ScanError/RemoteScanOptions), TargetRequest payload, target-aware
  IWmiQueryService, remote CimWmiQueryService + RemoteCimErrorMapper
- Slice 2: inventory per-host cache (migration AddHostToHardwareSnapshots),
  remote targets, adapters/GPUs/monitors/installed software, TargetSelector UI
- Slice 3: security scan context per target; LocalAdministratorsCheck
  repaired (nested CimInstance PartComponent — the string regex never matched
  under MMI); new checks UAC/Defender signatures/patch level/reboot pending/
  account policy (new ILocalAccountPolicyReader seam); host-scoped scan
  history (migration AddHostToSecurityScans with local backfill)
- Slice 4: diagnostics categories (Dns/System added), physical-vs-virtual
  adapter split with MAC/speed/DHCP, new diagnostics disk space/DNS server
  reachability/DC reachability/reboot pending/update recency; grouped UI;
  diagnostics stay local-only by design
- Slice 5: LdapErrorMapper (bind failed/DC down/timeout/naming context
  missing/DNS pre-probe), explicit credentials + domain/DC selection for AD
- Slice 6: BatchSecurityScanService (MaxParallelScans, connectivity gate,
  per-host outcomes, scope-per-host persistence) + first bridge events
  (IBridgeEventPublisher, security/batchScanProgress) + multi-host UI
- Gates per slice: dotnet build 0 warnings, all backend tests green
  (final: 218 across 8 assemblies), vitest 21/21, npm build; committed per
  slice (6 commits + docs)
- Open follow-ups recorded in TODO.md (StdRegProv remote registry, batch
  cancel channel, multi-host report, live remote validation)

### 2026-07-03 — Remote-analysis review fixes (five commits)
- P1: explicit-credential SecureString was disposed before the WSMan session
  authenticated (lazy connect) — now lives for the session lifetime;
  regression test drives the remote path against an unreachable loopback
- P2: WSMan logon-failure HRESULTs → AUTHENTICATION_FAILED, remote access
  denied → ACCESS_DENIED without requiredPrivilege (NTLM caveat documented
  in ADR 0007/README); ScanError maps AccessDenied to the Authenticate phase
- P2: SecurityPage never shows results/history for a different target than
  selected (hide + "load last saved scan"); batch panels only in multi mode
- P2 UX: inventory host list + single detail pane; shared CredentialFields
  grid (TargetSelector + AD form); AD subtitle no longer claims
  current-user-only
- P1 scope: diagnostics-remote acceptance point formally amended to a
  deliberate cut — WMI-transportable diagnostics recorded as follow-up in
  TODO.md; connectivity probes stay local by nature

### 2026-07-03 — Enterprise UX pass (goal-directed session, six commits)
- UX audit findings: five pages with five different header/action/error
  patterns; result-to-target relationship visible only on Security; coverage
  findings (LOCAL-ONLY/NOT-RUN) looked like security problems; diagnostics
  evidence always fully expanded; no summary metrics anywhere; three badge
  implementations; tables without overflow handling; primary button color
  inconsistent (sky vs slate)
- Shared primitives in frontend/src/shared/ui: Button, PageHeader,
  SummaryMetric, EmptyState/ErrorState, DetailsDisclosure, DataTable,
  EvidenceList; StatusBadge gained an info variant; content max-width
- Security: severity summary strip + host/status/timestamp context line;
  coverage notes separated from findings; batch rows compact with
  collapsible details
- Diagnostics: Pass/Warning/Fail/Not-run summary; next steps first on
  failing checks, evidence collapsed (open on FAIL); category headers with
  attention counts
- Inventory: System/CPU/Memory/Storage/Network/GPU/Monitors/Software/
  BitLocker grouping on DataTable; actionable remote error hint
- AD: overview/hygiene sections; per-error-code what-to-do hints; connection
  form explains its fields. Reporting: local-only scope explicit
- New tests: coverage-note separation (Security), diagnostics summary/error
  (DiagnosticsPage.test.tsx). Gates: tsc clean, vitest green, npm build,
  dotnet build 0 warnings, all backend tests green

### 2026-07-03 — Remote completion pass (goal-directed session, six commits)
- Inventory adapters: WMI unknown-speed sentinels normalize to null (backend
  + frontend guard for old cached rows), IPv4/IPv6 joined from
  Win32_NetworkAdapterConfiguration by adapter index, connected-first
  ordering, disconnected adapters collapsed
- IWmiQueryService.InvokeMethodAsync (static WMI class methods) implemented
  in CimWmiQueryService with the query path's session/DNS-probe/error
  mapping; proven live against local StdRegProv in an integration test
- Remote software inventory via StdRegProv (both bitness views, no
  Win32_Product, no SystemComponent filter remotely — would double round
  trips); failures become a structured SoftwareCaptureError on the snapshot
- Inventory persistence: listHosts/deleteHostSnapshot actions, cacheOnly
  reads restore stored hosts on page load without network traffic;
  multi-host scans bounded client-side by MaxParallelScans (from app info)
- Diagnostics: DiagnosticContext threading; remote-capable: domain
  membership, services, disk space (rewritten to Win32_LogicalDisk,
  IDriveInfoProvider seam deleted), update recency, reboot pending (local
  registry seam locally, StdRegProv remotely); connectivity probes return
  visible NOT_RUN/UnsupportedRemoteOperation for remote targets; UI uses the
  shared Local/Remote/Multiple flow with per-host summary + disclosure
- AD: NormalizeCredentials (UPN / DOMAIN\user / credential domain /
  directory-domain fallback / clear INVALID_REQUEST), testConnection action
  (RootDSE bind), UI warns before ambiguous binds and explains directory vs
  credential domain
- Not done deliberately: security registry checks still LOCAL-ONLY remotely
  (follow-up in TODO), event-log summary local (Win32_NTLogEvent too slow),
  live validation against a second machine still open

## Standing constraints (from loop definition)

- One small task per iteration; finish M1.1 before M2.
- Milestone completion ⇒ stop with NEEDS_USER_REVIEW, do not auto-start next.
- Read-only behavior everywhere until M6; ADR before any architectural change.
