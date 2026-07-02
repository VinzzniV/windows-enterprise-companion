# TODO — Backlog from the 2026-07-02 project review

Living document: check items off, reprioritize freely, delete what gets
rejected. Ordering within a section is by value/effort. Milestone work
(M6, M7) still follows the loop protocol: ADR + user decisions first.

## P1 — Hardening (secures everything that already exists)

- [x] **Wec.Host.Tests project** — 10 tests (2026-07-02):
  - [x] Composition-root test: builds the REAL host, resolves all
        `IActionHandler`s, asserts every module contributes one
  - [x] `ActionDispatcher` tests (success, unknown action, Result failure,
        malformed payload → INVALID_REQUEST, throwing handler →
        INTERNAL_ERROR without leaking the exception message)
  - [x] `BridgeJson` envelope tests (camelCase, SCREAMING_SNAKE enums,
        request deserialization)
- [x] **App-start smoke test in CI** (2026-07-02) — runs the published exe on
      master/tag pushes, asserts "WebView2 initialized" in the log
- [x] **React ErrorBoundary** per route (2026-07-02) — render errors show an
      error card with "Try again"; navigation resets the boundary
- [x] Add `frontend/*.tsbuildinfo` to `.gitignore` and untrack (2026-07-02)
- [x] Bump `<Version>` in `Directory.Build.props` → 0.2.0 (2026-07-02)
- [x] **Dependabot** for NuGet + npm + GitHub Actions, weekly, grouped
      (2026-07-02)
- [x] Bump GitHub Actions majors (2026-07-02): checkout v7, setup-node v6,
      setup-dotnet v5, upload-artifact v7

## P1 — Pending decisions (user)

- [ ] Sign off **ADR 0004** (cross-module read contracts) — Proposed since M5
- [ ] Review **ADR 0006** (AD access strategy) — implemented, needs sign-off
- [ ] Decide: TypeScript type generator for `api-types.ts` (manual sync is
      ~20 types across 6 modules now; drift only surfaces at runtime) — was
      deliberately out of scope in M1, worth revisiting

## P2 — Quick wins on existing data

- [x] **Security scan history UI** (2026-07-02) — `security/getScanHistory`
      (summaries with severity counts + diff latest↔previous keyed by
      FindingId+AffectedResource), history section with dependency-free SVG
      sparkline, `Wec:Security:HistoryLimit` option (default 20), 9 new tests
- [ ] **AD section in the executive summary report** — missing since M4.
      Options (pick one): explicit "include live AD analysis" checkbox
      (visible run, consistent with the no-silent-queries rule) or AD scan
      persistence analogous to security scans
- [x] **"Restart as administrator" button** (2026-07-02) — sidebar footer,
      only visible unelevated; launches an elevated copy of this exe via UAC
      ("runas"), dismissed prompt = valid outcome, then closes the unelevated
      instance; 4 handler tests

## P3 — Mid-term features

- [x] **Frontend component tests** (2026-07-02) — Testing Library added;
      SecurityPage (findings, severity + category filters, empty state),
      ReportingPage (export success/cancel, disabled without data),
      ErrorBoundary; vitest setup file with explicit cleanup
- [ ] **Inventory expansion**: network adapters, GPU, installed software
      (registry uninstall keys, NOT Win32_Product), monitors
- [ ] **Hardware snapshot history** — keep more than the latest snapshot,
      show "what changed since last capture"
- [ ] **Settings page** — UI for the tunables currently only editable by hand
      in `%APPDATA%\Wec\usersettings.json` (cache TTL, AD thresholds, log
      level); would be the first legitimate write path before M6
- [ ] **In-app log viewer** with CorrelationId filter
- [ ] Module READMEs for Security, Diagnostics, Reporting (CLAUDE.md requires
      one per module; only Inventory and ActiveDirectory have one)
- [ ] **Scheduled/baseline scans** — security scan via Task Scheduler
      (headless mode) + comparison against a saved baseline

## P4 — Roadmap & externals

- [ ] **M6 — Controlled remediation** (ends the read-only era): ADR first
      (action catalog, preview, confirmation UX, risk levels, audit log),
      then user decisions, then slices
- [ ] **M7 — AI assistant** (optional): explain findings, cite structured
      data, never execute; ADR + opt-in required
- [ ] **Code signing** once a certificate is budgeted → then MSIX/winget
      distribution becomes viable (removes SmartScreen warning)
- [ ] Verify the AD module against a real test domain (currently only proven
      against mocked fixtures; dev machine is workgroup-joined)
