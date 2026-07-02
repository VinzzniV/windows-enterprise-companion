# Autonomous Loop State

Maintained by the autonomous development loop. One entry per iteration.

## Current position

- **Milestone:** M1.1 — Stabilization and UX hardening — **COMPLETE**
  (awaiting user review before M2 starts)
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

## Standing constraints (from loop definition)

- One small task per iteration; finish M1.1 before M2.
- Milestone completion ⇒ stop with NEEDS_USER_REVIEW, do not auto-start next.
- Read-only behavior everywhere until M6; ADR before any architectural change.
