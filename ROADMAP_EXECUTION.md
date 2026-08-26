# Windows Enterprise Companion — Roadmap Execution

Status: `IN_PROGRESS`

Current phase: Phase 2 — Reduce Diagnostics to Device Health

Current slice: Add remote Event Log summary support

## Done

- Roadmap, decisions, AGENTS rules and relevant accepted ADRs reviewed.
- Current affected modules, frontend workspaces, persistence and CI/release
  surfaces inventoried.
- Product, privacy, Git and release decisions D-001 through D-008 recorded.
- Autonomous execution and safety rules added to `AGENTS.md`.
- Multi-host batch capability is assigned to the Clients workspace before
  standalone legacy wrappers are removed.
- Offline/home execution rules and company-environment release gates are
  defined.
- Explicit autonomous implementation start received on 2026-08-26.
- `origin/master` re-fetched and verified at
  `aba4ccd57f724cb359e9ac643378bf6ada0ce559` before creating
  `codex/ultimate-admin-roadmap`.
- Roadmap controls committed on `codex/ultimate-admin-roadmap`.
- Frontend dependency lock updated to React Router 7.18.2, PostCSS 8.5.26
  and nanoid 3.3.18; all 415 frontend tests, the production build and a
  zero-finding NPM audit passed.
- CI now fails on High- or Critical-Severity NPM findings after the locked
  frontend install; the gate passes against the updated lockfile.
- Removed 82 obsolete GitHub Actions artifacts (5.14 GiB) and retained the ZIP
  and installer from the latest successful master packaging run. Release
  `v0.1.0` and its two published assets were verified unchanged.
- Master package artifacts now expire after three days; tag runs publish the
  versioned files directly as GitHub Release assets instead of duplicating
  them in Actions storage.
- The package workflow validates required ZIP contents and creates verified
  SHA-256 files for ZIP and installer. A manual workflow dispatch can exercise
  publish, host smoke, packaging and artifact upload without creating a tag or
  release.
- A local self-contained 0.2.0 publish produced 578 files; the portable ZIP
  was expanded, required host/frontend files were found and its SHA-256 record
  was verified. Inno Setup remains a GitHub-CI-only check on this host.
- Phase 0 local gates passed: Release build with zero warnings/errors, 701
  backend tests, 415 frontend tests, 308 generated contracts current,
  production build, zero-finding NPM audit, module dependency check, committed
  secret-pattern scan and real Desktop/Clients-route smoke test.
- Draft PR #28 is open. Its pull-request CI run `33013654432` passed. Manual
  packaging run `33013664987` passed publish, host smoke, ZIP validation, Inno
  Setup installer and package checksums before the upload gate.
- ADR 0018 records the reduced Device Health scope, Client 360 read behavior,
  historical Diagnostics retention, Network Scan separation and the bounded
  multi-host move into Clients.
- ADR 0019 records AD-authoritative read-only User Management, stable
  `objectGUID` identity, evidence-aware user/client correlation, the Leaver-first
  lifecycle sequence and preservation of all five legacy Employee Lifecycle
  tables.
- Phase 1 gates passed: Release build without warnings, 701 backend tests, 415
  frontend tests, 308 generated contracts current, production build, zero NPM
  findings and no production-module dependency violations. PR CI run
  `33015033285` passed for commit `540cc08`.
- Characterization coverage now fixes the contracts for Windows Update age,
  service state, Event Log summary, disk free space and preset-based Event Log
  queries.
- Diagnostics registrations, options, implementations and tests for network,
  DNS, domain/DC, time-synchronization and pending-reboot checks were removed.
  The four retained Health checks, detailed Event Log action, persistence and
  bridge names remain intact; Network Scan is unchanged.

## Blocked

- Phase 0 artifact upload is waiting for GitHub's storage-usage recalculation.
  Run `33013664987` reports that recalculation takes 6–12 hours after cleanup;
  two preserved artifacts currently use 132.24 MiB. Retry the manual workflow
  after the quota state updates.

## Deferred release gates

- AD, Kaspersky, opsi and Nessus live validation in the company environment.
- Remote Inventory, Health, Event Log, Ping and WinRM smoke tests against a
  designated non-critical client.
- Explicit approval for version tag, installer publication and GitHub Release.

## Last verification

- Baseline commit: `aba4ccd57f724cb359e9ac643378bf6ada0ce559`.
- Phase 1 Release build: 0 warnings, 0 errors.
- Phase 1 backend tests: 701 passed.
- Phase 1 frontend tests: 415 passed.
- Phase 1 bridge contracts: 308 generated types current.
- Phase 1 frontend production bundle: 535.98 kB JavaScript,
  155.62 kB gzip.
- Phase 1 NPM audit: zero vulnerabilities.
- Module dependency rules, committed secret-pattern scan and latest WEC log
  sensitive-term scan passed.
- Local Desktop startup and Dashboard-to-Clients navigation passed against
  the Release host.
- Phase 2 reduction build passed without warnings; the reduced Diagnostics
  suite has 23 passing tests and Host has 70 passing tests.
- GitHub CLI is authenticated; 82 obsolete Actions artifacts (5.14 GiB) were
  removed and the two preserved artifacts use 132.24 MiB.
- Local Inno Setup compiler is unavailable; installer verification relies on
  GitHub CI.
- `%APPDATA%\Wec\usersettings.json` is not present on this host.

## Next

1. Add WMI-backed remote Event Log summaries and compatibility coverage for
   retained historical `diagnostics_runs` payloads.
2. Rename the user-visible Diagnostics workspace to Health.
3. Retry the Phase 0 manual package upload after GitHub recalculates storage
   usage.
