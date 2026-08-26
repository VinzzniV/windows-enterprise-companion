# Windows Enterprise Companion — Roadmap Execution

Status: `IN_PROGRESS`

Current phase: Phase 6 — User Management read-only foundation

Current slice: Extend the AD user projection with stable lifecycle fields

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
- Remote Event Log summaries now use the existing credential-aware WMI seam,
  retain bounded evidence and report source failures as typed `NOT_RUN`
  results.
- Historical `diagnostics_runs` payloads remain physically unchanged. The
  reader returns only current Health check codes, while explicit persisted enum
  values keep old JSON deserializable. Retired categories were removed from the
  current generated TypeScript contract.
- The client tab and all user-visible copy now say Health. Backend project,
  bridge actions, persistence table and the compatible `section=diagnostics`
  deep-link key remain unchanged as required by ADR 0018.
- Phase 2 full gates passed: Release build without warnings, 656 backend tests,
  415 frontend tests, 308 generated contracts current, production build, zero
  NPM findings, module dependency check and production credential scan.
- PR CI run `33016855845` passed for the Phase 2 milestone commit `733a919`.
- Narrow Core projections now expose the latest persisted hardware, installed
  software, Health and Security evidence without introducing module references.
- Client 360 composes those projections through `clients/getOverview` and shows
  source freshness, completeness, coverage, hardware/OS, software, Health and
  Security summaries with detail-tab links.
- Opening Client 360 starts no remote scan. AD/Kaspersky/opsi/Nessus context and
  relationship-map connectivity are both explicit read-only actions.
- Phase 3 full gates passed: Release build without warnings, 665 backend tests,
  418 frontend tests, 320 generated contracts current, production build, zero
  NPM findings, module dependency check and production credential scan.
- One authoritative route registry now owns route, navigation, section-label
  and future navigation-search metadata without changing existing URLs.
- All route workspaces, including client detail and legacy compatibility
  routes, load through React lazy imports behind one Suspense state and the
  existing per-path Error Boundary. No manual chunk configuration was needed.
- Phase 4 tests cover route metadata, lazy navigation, the shared loading state
  and rejected chunk imports. The initial JavaScript chunk fell from 547.28 kB
  (157.47 kB gzip) to 271.56 kB (86.57 kB gzip).
- Phase 4 full gates passed: Release build without warnings, 665 backend tests,
  423 frontend tests, 320 generated contracts current, production build, zero
  NPM findings, module dependency check, production credential scan and a real
  Release desktop Dashboard-to-Clients lazy-route smoke test.
- Current AD, Kaspersky, opsi and Nessus source-state precedence is fixed by
  characterization tests before changing the relationship presentation.
- A bounded frontend relationship model now carries stable entity keys,
  semantic state, observation time, deep links and evidence-bearing edges with
  source, confidence and explanation. It deliberately has no persistence or
  generic graph engine.
- Client 360 now uses the accessible map/list presentation for AD, Kaspersky,
  opsi, Nessus, WEC Inventory, Health and Security context. Decorative dragging
  and its 65 lines of map animation CSS were removed; connectivity remains an
  explicit action and a missing Ping/WinRM response is not called offline.
- Phase 5 full gates passed: Release build without warnings, 665 backend tests,
  437 frontend tests, 320 generated contracts current, production build, zero
  NPM findings, module dependency check and production credential scan.

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
  suite has 26 passing tests and Infrastructure has 138 passing integration
  tests.
- Phase 2 final backend suite: 656 passed. The intentional reduction from 701
  removes tests for the retired checks and adds retained-check, remote Event Log
  and historical-payload coverage.
- Phase 2 final frontend suite: 415 passed. Production bundle: 535.82 kB
  JavaScript, 155.55 kB gzip. NPM audit reports zero vulnerabilities.
- Phase 3 final backend suite: 665 passed. Phase 3 frontend suite: 418 passed.
  The pre-lazy-loading production bundle is 547.28 kB JavaScript and 157.47 kB
  gzip; this is the Phase 4 comparison baseline.
- Phase 3 Release desktop smoke reached Dashboard and the empty Clients
  workspace without startup, layout or navigation failures. The local profile
  contains no client, so the Client 360 rendering gate is covered by frontend
  tests with complete, stale, partial and missing stored evidence.
- Phase 4 frontend suite: 423 passed. The route-split production build emits a
  271.56 kB initial JavaScript chunk (86.57 kB gzip), down 50.4% and 45.0%
  respectively from the Phase 3 baseline; feature chunks range from 0.31 kB to
  59.73 kB.
- Phase 4 Release desktop smoke loaded Dashboard and then Clients from the
  built local assets without a blank screen or route error. The home profile
  reported all four management sources as not configured and ran no explicit
  connectivity action.
- Phase 5 component and Client 360 integration tests cover all current source
  states, bounded nodes, edge evidence, observation timestamps, confidence,
  keyboard-accessible deep links, semantic list fallback and manual-only
  connectivity. The home profile has no client record, so a real Relationship
  Map rendering remains part of the company-environment smoke gate.
- Phase 5 frontend suite: 437 passed. The initial JavaScript chunk remains
  271.56 kB (86.57 kB gzip); removal of the decorative map CSS reduces the
  stylesheet from 61.85 kB to 51.58 kB.
- Release desktop smoke reached Dashboard and Clients without starting any
  company or remote query. The Health tab itself could not be opened on this
  home profile because the Clients list contains no device; its tab navigation
  and content are covered by frontend tests.
- GitHub CLI is authenticated; 82 obsolete Actions artifacts (5.14 GiB) were
  removed and the two preserved artifacts use 132.24 MiB.
- Local Inno Setup compiler is unavailable; installer verification relies on
  GitHub CI.
- `%APPDATA%\Wec\usersettings.json` is not present on this host.

## Next

1. Extend the AD user projection with stable object identity and lifecycle
   fields while preserving current directory analysis contracts.
2. Add deterministic server-side user search, filtering, sorting and paging.
3. Create the read-only User Management module and User 360 read model without
   writing lifecycle data or modifying AD.
4. Build the Users workspace and User 360 Overview/Access tabs.
5. Add user results to global search when the global-search slice lands.
6. Retry the Phase 0 manual package upload after GitHub recalculates storage
   usage.
