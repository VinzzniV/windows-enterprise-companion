# TODO — Backlog from the 2026-07-02 project review

Living document: check items off, reprioritize freely, delete what gets
rejected. Ordering within a section is by value/effort. Milestone work
(M6, M7) still follows the loop protocol: ADR + user decisions first.

## Done 2026-07-03 — Remote analysis (ADR 0007, ADR 0006 revision)

Local + remote read-only analysis shipped in six slices: Core target/
credential types with WSMan remote CIM and typed remote errors; inventory
remote with per-host cache and expanded capture (adapters/GPU/monitors/
software); security remote with repaired local-admins parsing, five new
checks and host-scoped history; categorized diagnostics with five new
checks; AD with diagnostic LDAP errors, explicit credentials and domain/DC
selection; parallel multi-host security scans with per-host progress events
(first use of the bridge event channel).

Follow-ups spawned by that work:

- [ ] Remote path for the registry-based checks (RDP, Secure Boot, UAC,
      reboot pending) via StdRegProv method invocation — needs an
      IWmiQueryService method-invoke extension
- [ ] Batch-scan cancellation from the UI (bridge needs a cancel channel)
- [ ] Multi-host executive summary report (Reporting reads local data only)
- [ ] Verify remote scans against a real second machine/test domain (error
      mapping is unit-tested; WSMan HRESULT paths not yet proven live)
- [ ] Remote-capable diagnostics for the WMI-transportable checks (services,
      event logs, disk space, reboot pending, update recency). **Deliberate
      scope cut from the remote-analysis goal:** Diagnostics shipped
      local-only because the connectivity probes (ping, DNS, DC reachability,
      time) inherently measure *this* machine; the WMI-based subset can gain
      targets in its own slice.

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

- [x] **ADR 0004** accepted (2026-07-02)
- [x] **ADR 0006** accepted (2026-07-02)
- [x] Decision (2026-07-02): **build the TypeScript type generator** —
      C#→TS as a build step with a CI diff check → new P2 item below

## P2 — Quick wins on existing data

- [x] **Security scan history UI** (2026-07-02) — `security/getScanHistory`
      (summaries with severity counts + diff latest↔previous keyed by
      FindingId+AffectedResource), history section with dependency-free SVG
      sparkline, `Wec:Security:HistoryLimit` option (default 20), 9 new tests
- [ ] **AD section in the executive summary report** — decided 2026-07-02:
      explicit "include live AD analysis" checkbox in the export UI (visible
      run, no silent queries, no new persistence); report renders overview +
      hygiene sections when checked
- [ ] **TypeScript type generator** for `api-types.ts` — decided 2026-07-02:
      small C#→TS generator tool run as build step, CI fails on diff
      (kills the manual-sync drift class)
- [x] **"Restart as administrator" button** (2026-07-02) — sidebar footer,
      only visible unelevated; launches an elevated copy of this exe via UAC
      ("runas"), dismissed prompt = valid outcome, then closes the unelevated
      instance; 4 handler tests

## P3 — Mid-term features

- [x] **Frontend component tests** (2026-07-02) — Testing Library added;
      SecurityPage (findings, severity + category filters, empty state),
      ReportingPage (export success/cancel, disabled without data),
      ErrorBoundary; vitest setup file with explicit cleanup
- [x] **Inventory expansion** (2026-07-03): network adapters, GPU, installed
      software (registry uninstall keys, NOT Win32_Product), monitors
- [ ] **Hardware snapshot history** — keep more than the latest snapshot,
      show "what changed since last capture"
- [ ] **Settings page** — UI for the tunables currently only editable by hand
      in `%APPDATA%\Wec\usersettings.json` (cache TTL, AD thresholds, log
      level); would be the first legitimate write path before M6
- [ ] **In-app log viewer** with CorrelationId filter
- [x] Module READMEs for Security, Diagnostics, Reporting (2026-07-03; all
      five modules documented)
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
