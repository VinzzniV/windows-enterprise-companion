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
- [ ] **App-start smoke test in CI** — launch the published exe on the Windows
      runner, wait ~10 s, assert "WebView2 initialized" appears in the log;
      catches the startup-crash class unit tests structurally cannot see
- [ ] **React ErrorBoundary** at app level — a render error in one page must
      show an error card, not blank the whole app
- [ ] Add `frontend/*.tsbuildinfo` to `.gitignore` and untrack (build artifacts)
- [ ] Bump `<Version>` in `Directory.Build.props` before the next release tag
      (0.1.0 is already published; the CI tag guard enforces the match)
- [ ] **Dependabot/Renovate** for NuGet + npm + GitHub Actions — ADR 0005 made
      runtime patching our duty; nobody reminds us otherwise
- [ ] Bump GitHub Actions majors when Node-22-based versions ship
      (checkout/setup-node/setup-dotnet/upload-artifact are Node-20-deprecated)

## P1 — Pending decisions (user)

- [ ] Sign off **ADR 0004** (cross-module read contracts) — Proposed since M5
- [ ] Review **ADR 0006** (AD access strategy) — implemented, needs sign-off
- [ ] Decide: TypeScript type generator for `api-types.ts` (manual sync is
      ~20 types across 6 modules now; drift only surfaces at runtime) — was
      deliberately out of scope in M1, worth revisiting

## P2 — Quick wins on existing data

- [ ] **Security scan history UI** — scans are already fully persisted
      (`security_scans`), the UI only shows the latest: add a history view
      with diff to the previous scan (new/resolved findings) and a trend line
- [ ] **AD section in the executive summary report** — missing since M4.
      Options (pick one): explicit "include live AD analysis" checkbox
      (visible run, consistent with the no-silent-queries rule) or AD scan
      persistence analogous to security scans
- [ ] **"Restart as administrator" button** — user-initiated relaunch with
      UAC prompt (ADR-0002-conform, no auto-elevation); removes the biggest
      elevation UX friction

## P3 — Mid-term features

- [ ] **Frontend component tests** — at least SecurityPage (filters) and
      ReportingPage (export states) with a mocked `invoke`
- [ ] **Inventory expansion**: network adapters, GPU, installed software
      (registry uninstall keys, NOT Win32_Product), monitors
- [ ] **Hardware snapshot history** — keep more than the latest snapshot,
      show "what changed since last capture"
- [ ] **Settings page** — UI for the tunables currently only editable by hand
      in `%APPDATA%\Wec\usersettings.json` (cache TTL, AD thresholds, log
      level); would be the first legitimate write path before M6
- [ ] **In-app log viewer** with CorrelationId filter
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
