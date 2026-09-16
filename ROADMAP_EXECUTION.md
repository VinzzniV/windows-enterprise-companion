# Windows Enterprise Companion — Roadmap Execution

Status: `LIVE_ACCEPTANCE_FINDINGS_OPEN`

Current phase: Object-centered consolidation (ADR 0022), phases 0–7 and 9.
Phase 8 Windows identity collection remains excluded.

Current slice: Live acceptance completed with an unresolved AD sorting defect
(F-01). The implementation milestone, G2 review, automated regression and
CI/packaging evidence belong to build `17f53ef`; they do not establish live
acceptance. No release or tag is authorized.
Follow-up on 2026-09-16: bounded checks ran against the single user-designated
workstation under D-003, existing configured sources and the available
Microsoft 365 tenant. The user entered the admin credential directly in WEC.
Remote Inventory/Health/Event Log/connectivity, cloud identity/relationship/
license journeys, narrow-window navigation and sign-out/cache clearing passed.
AD direct-member reads failed because the DC rejected the two-key server-sort
control. Full Nessus coverage and a real cross-tenant switch remain unverified.
Evidence and required correction: `docs/consolidation-live-acceptance.md`.
Branch: `codex/object-centered-consolidation`, created from clean `184a10f`.
Binding scope: `docs/consolidation-analysis-and-plan.md`; preservation and
verification details: `docs/consolidation-verification.md`.

## Current consolidation verification (2026-09-16)

- All 1,025 backend tests and 559 frontend tests in 97 files pass. The Release
  build has zero warnings/errors, the 494 generated contracts are current, and
  the frontend production build passes after a locked dependency install.
- Dependency rules pass across all 20 production projects. The NPM High gate
  passes; two pre-existing Moderate Vitest development-tool findings remain.
- Devices, Users and Groups share a bounded memory-only working set and search.
  Entra-only accounts, source-native groups, exact Windows targets and raw
  management records remain separate unless scoped identity evidence proves
  a relationship. Conflicts, unknown values, independent source states and
  targeted explicit reads remain visible.
- C2 navigation follows verified G2 preservation. Existing posture/batch,
  comparison, Cleanup, Leaver, Windows tools, exports and gated Patch/Print
  operations remain reachable; old routes preserve subject and section.
- Closure fixes retain anonymous/duplicate Nessus report rows, source-specific
  Intune states, group classification, separate AD/Entra filters, saved/scanned
  filters and SKU navigation in both directions. Device profiles expose the
  seven specified focused areas.
- A 10,000-record fixture retains exactly 5,000 rows, preserves the source
  total and marks partial coverage. Capture took 61 ms and filter/page 9 ms in
  this local run; these observations are not performance guarantees.
- An isolated local Release host initialized WebView2 and the migrated database.
  Normal-width navigation, empty Devices state, filters and keyboard search
  were inspected. No WEC-owned TCP listener or Error/Fatal log entry appeared.
  The original desktop check ended on the user's physical Escape. The later
  explicitly authorized live follow-up passed the narrow-window journeys.
- The existing Dashboard performed its bounded read-only opsi status/product
  reads using the configured connection. This is not full opsi acceptance.
  The later live follow-up exercised AD/KSC/opsi/Nessus, Graph/Intune and the
  designated remote client. AD identity succeeded but direct members failed
  (F-01); Nessus coverage remained Partial. See the live acceptance report for
  per-journey evidence and limits. No remote write or release was performed.

## Previous program history

The entries below concern the preceding roadmap and are historical context,
not a competing current slice or the validation evidence for consolidation.
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
- A stable, server-paged AD user inventory and read-only User Management module
  now expose User 360 identity, lifecycle and SID-validated access context.
- The Users workspace provides bounded search/filter/sort/paging and
  Overview/Access detail tabs without persisting workflow state or credentials.
- Global search opens with `Ctrl+K`, supports keyboard and focus management,
  and combines navigation, saved targets, stored clients plus bounded AD user
  and client lookups. Its first version exposes no write actions.
- Phase 6 full gates passed: Release build without warnings, 680 backend tests,
  453 frontend tests, 334 generated contracts current, production build, zero
  NPM findings, module dependency check and production credential scan.
- ADR 0019 and the Inventory module documentation now define the approved,
  bounded interactive-user and local-profile evidence, including explicit
  system/service-profile filtering, latest-snapshot retention and the rule
  that observations never become ownership claims.
- Explicit Inventory scans now collect only the approved domain-user and
  profile SID evidence. Older snapshots remain readable and visibly report
  that the evidence was not captured.
- A narrow Core provider joins directory users to Inventory devices only by
  exact SID and reports evaluated, missing, unavailable and truncated source
  coverage without guessing from account, e-mail or host names.
- User 360 now shows a bounded linked-device view with stored software,
  Health, Security and Nessus summaries. A dedicated stored-only Nessus
  provider prevents profile reads from starting foreground or background
  synchronization.
- The User relationship map reuses the accessible bounded map/list pattern,
  exposes relationship type, source, observation time, confidence and
  explanation, and links into Client 360 and its detail views.
- Phase 7 full gates passed: Release build without warnings, 699 backend tests,
  457 frontend tests, 352 generated contracts current, production build, zero
  NPM findings, module dependency check and production credential scan. PR CI
  run `33025884397` passed for milestone commit `c5efe14`.
- User 360 now has a deliberately selected read-only Leaver review that exposes
  account state, replicated activity, direct and SID-allowlisted privileged
  groups, device evidence, source coverage and unresolved physical return.
- Review marks live only in the current frontend session. The bounded Markdown
  export requires an explicit save-dialog confirmation, persists no case state
  and carries explicit evidence boundaries rather than completion claims.
- Phase 8 full gates passed: Release build without warnings, 704 backend tests,
  462 frontend tests, 354 generated contracts current, production build, zero
  NPM findings, module dependency check and production credential scan.
- Phase 8 PR CI run `33026958213` passed for milestone commit `1a42320`.
- ADR 0020 records a computed, non-persisted Action Center read model with
  narrow Core evidence projections, deterministic item keys, explicit source
  coverage and allowlisted deep links. Workflow state and a generic provider or
  rules engine remain out of scope.
- Hygiene assessment policy was extracted without changing public behavior.
  Request-bound hygiene evidence, stored Inventory evidence and stored-only
  Security findings now expose bounded Action Center projections without
  triggering Inventory or Security scans.
- The new Action Center module computes a read-only, server-filtered, sorted and
  paged work list with severity, evidence age, coverage, reliability and a
  recommended next action. It persists neither source payloads nor work items.
- The Action Center workspace uses a dense table rather than dashboard cards.
  Its optional context view contains only the selected device, reporting source
  and evidence edge, and reuses the accessible map/list presentation.
- Phase 9 full gates passed: Release build without warnings, 715 backend tests,
  465 frontend tests, 365 generated contracts current, production build, zero
  NPM findings, module dependency check and committed credential scans.
- Phase 9 PR CI run `33028585766` passed for milestone commit `7e60401`.
- A dedicated Device Cleanup module now composes request-cached hygiene facts,
  stored Inventory timestamps and approved stored user/device observations.
  It starts no Inventory, Health, Security or connectivity scan when opened.
- Cleanup classification is conservative and explainable: an existing critical
  stale-source finding produces `PotentialCleanup`; disabled, stale, orphan or
  old-Inventory evidence produces `Review`. Recent evidence remains visible but
  never silently overrides a conflicting source fact.
- The guided workspace exposes AD state and replicated last activity,
  Kaspersky and opsi last seen, Nessus scan age, WEC Inventory age, source
  coverage and user relationship evidence. Ping/WinRM runs only from its
  explicit button and a missing response is not treated as retirement proof.
- Manual decision, reason, reviewed-source marks and connectivity state remain
  frontend-session-only. A reason is required before an explicitly confirmed,
  bounded Markdown export; no AD disable, move or delete action exists.
- Stale Action Center items now deep-link into the selected cleanup assessment;
  unrelated security, patch and vulnerability items keep their specialist
  destinations.
- Phase 10 full gates passed: Release build without warnings, 724 backend tests,
  468 frontend tests, 376 generated contracts current, production build, zero
  NPM findings, module dependency check and committed credential scans.
- Phase 10 PR CI run `33029981576` passed for milestone commit `47937d3`.
- Inventory, Security and Health now expose bounded, cancellable multi-host
  handlers with typed progress and isolated per-host outcomes. Clients owns the
  only multi-host scan workbench; row selection alone never starts a scan and
  session credentials are displayed only as identity metadata.
- Inventory, Security and Health result components were separated from their
  old standalone pages before the replaced wrappers and full `TargetSelector`
  were removed. Shared credential fields remain in a narrow credential seam.
- The old unrouted Employee detail/form CRUD was removed after the read-only
  User Management MVP. The allowlisted lifecycle redirect, backend source
  types and all historical Employee Lifecycle tables remain preserved.
- `HygieneSourceLoader` now owns external source I/O, credentials and source
  states while `ItHygieneService` retains correlation and snapshot assembly.
  Winget preview/update planning and package execution are isolated behind the
  existing orchestration facade without changing confirmation or audit rules.
- Phase 11 full gates passed: Release build without warnings, 731 backend
  tests, 458 frontend tests, 386 generated contracts current, production
  build, zero NPM findings, module dependency check and committed credential
  scan.
- Phase 11 milestone PR CI run `33033955651` passed for commit `42aef05`.
- The final roadmap audit found one cross-phase omission: Client 360 did not
  yet surface the approved stored user/device evidence. Commit `59dd9dd`
  closes that gap with a narrow Inventory provider, explicit availability,
  source, observation time and confidence, without an AD query or ownership
  claim.
- The requirement-by-requirement completion audit confirms phases 0–11 in the
  current repository. The frozen Employee Lifecycle backend source and its
  five historical tables remain deliberately preserved under ADR 0019; no
  destructive migration was introduced.
- Final local gates on `59dd9dd` passed: Release build with zero warnings and
  errors, 733 backend tests, 459 frontend tests, 388 generated contracts,
  production build, zero NPM vulnerabilities, modular-monolith dependency
  rules and the committed production secret-literal scan.
- The final Release desktop smoke loaded Dashboard, opened the keyboard-driven
  global search with `Ctrl+K`, navigated to the lazy Clients workspace and
  showed all company providers as not configured. Navigation and selection
  triggered no scan. The smoke interval contained no Error/Fatal entry,
  credential payload field, SID, UPN or account payload field.
- Final-head PR CI run `33035939447` passed on `b9d153a`. The branch was
  re-fetched against `origin/master`, is current with the base, conflict-free
  and has no open review finding.
- Release-free packaging run `33036396522` on `b9d153a` passed audit, build,
  contract verification, all frontend and backend tests, self-contained
  publish, published-host smoke, ZIP creation and validation, Inno Setup
  installer creation and checksum verification.
- GitHub release-free packaging run `33112423083` passed every gate on final
  implementation head `1025511`: dependency audit, build, contract check, all
  tests, self-contained publish, published-host smoke, ZIP validation, Inno
  Setup installer, checksums and both artifact uploads. The release step was
  skipped as required.
- Downloaded artifacts `9663342701` and `9663344878` were independently
  verified. SHA-256 matched for `wec-0.2.0-win-x64.zip`
  (`39eca9f2ad607301e38eccff67c333055cd8509a7ed52cfd828f009dd9b08b6b`)
  and `wec-0.2.0-setup.exe`
  (`daa29b247ce09bd8dd4f756a81e3676a7e1913e1c5839b9cc7eaf565687f78df`).
  The ZIP contains 619 entries including the host executable, frontend entry
  point and application settings. Release `v0.1.0` remains unchanged.
- Device Cleanup now exports every candidate matching the active filter to one
  filterable `.xlsx` workbook rather than exporting only the visible page. The
  workbook carries device description with source provenance, classification,
  separate AD, Kaspersky, opsi, Nessus and WEC Inventory states and timestamps,
  relevant-finding counts, source coverage and explicit truncation metadata.
- The export performs one explicit, cancellable ICMP echo per exported device
  only after the save dialog is confirmed. Timeout and bounded parallelism are
  configuration values. It performs no WinRM check, retry, source refresh,
  persistence or directory write, and a missing reply is explicitly
  inconclusive rather than an offline or deletion claim.
- Device Cleanup list rows now expose the source-backed description. Generated
  contracts, backend behavior and frontend integration tests cover the full
  filtered export, description precedence, typed UTC spreadsheet dates,
  unavailable evidence, workbook validation and cancellation behavior.

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
- Phase 6 production build emits a 281.78 kB initial JavaScript chunk
  (89.82 kB gzip); route workspaces remain split into feature chunks.
- Phase 6 Release desktop smoke loaded Dashboard, opened the focused global
  search with `Ctrl+K`, and navigated through it to Users. The home profile
  showed the explicit no-domain state and made no company-system query.
- The Phase 6 smoke log contained no Error/Fatal entries or credential terms.
- Phase 7 frontend tests cover exact evidence labels, confidence, bounded
  presentation, semantic map/list behavior, stored device summaries, missing
  source states, deep links and the absence of profile-triggered scans.
- Phase 7 production build emits a 281.82 kB initial JavaScript chunk
  (89.85 kB gzip); User 360 and the shared Relationship Map remain lazy feature
  chunks.
- Phase 7 Release desktop smoke loaded Dashboard and navigated through
  `Ctrl+K` to Users. The home profile showed the explicit no-domain state; the
  smoke log contained no Error/Fatal, credential, SID, UPN or account-name
  terms.
- Phase 8 production build emits a 281.85 kB initial JavaScript chunk
  (89.87 kB gzip); User 360 remains route-split. The real Release host loaded
  local assets and navigated through `Ctrl+K` to Users. No Error/Fatal,
  credential, SID, UPN or account-name term was emitted in the smoke interval.
- Phase 9 production build emits a 282.41 kB initial JavaScript chunk
  (90.05 kB gzip); the Action Center is a separate 9.82 kB chunk (3.79 kB
  gzip). The real Release host navigated through `Ctrl+K` to Action Center and
  completed `actioncenter/listItems`. No Error/Fatal, credential, SID, UPN or
  account-name term was emitted in the smoke interval.
- Phase 10 production build emits a 282.99 kB initial JavaScript chunk
  (90.23 kB gzip); Device Cleanup is a separate 16.08 kB chunk (5.33 kB gzip).
  The real Release host navigated through `Ctrl+K` to Device Cleanup and
  completed `devicecleanup/listCandidates` without a connectivity probe. No
  Error/Fatal, credential, SID, UPN or account-name term was emitted in the
  smoke interval.
- Phase 11 production build emits a 283.10 kB initial JavaScript chunk
  (90.27 kB gzip). The real Release host loaded Dashboard and the Clients
  workbench from local assets; the home profile performed no remote scan.
  The smoke log contained no Error/Fatal, credential or user-payload terms.
- The post-audit production build emits a 283.10 kB initial JavaScript chunk
  (90.26 kB gzip); Client Detail remains split at 51.37 kB (13.55 kB gzip).
  Final suites contain 733 backend and 459 frontend tests, and 388 generated
  bridge types are current.
- GitHub CLI is authenticated; all 84 obsolete Actions artifacts (about
  5.27 GiB) were removed. The repository now reports zero Actions artifacts,
  while the two `v0.1.0` GitHub Release assets remain published and unchanged.
- Local Inno Setup compiler is unavailable; installer verification relies on
  GitHub CI.
- `%APPDATA%\Wec\usersettings.json` is not present on this host.
- Final release-free GitHub packaging run `33112423083` passed on `1025511`.
  ZIP artifact `9663342701` is 91,969,707 bytes and installer artifact
  `9663344878` is 62,930,001 bytes. Both downloaded packages match their
  committed SHA-256 records; no tag or release was created.
- Device Cleanup Excel export baseline is
  `e89395fe874d64aa09ad185185df804390025e95`. Final local verification passes a
  warning-free Release build, all 739 backend tests, all 461 frontend tests,
  390 generated bridge contracts, the production frontend build and the High
  severity NPM audit gate. Two existing Moderate Vitest advisories remain
  deferred because the available remediation requires a breaking major update.
- The workbook test saves with Open XML validation enabled and reopens the
  resulting file. Device Cleanup export coverage includes complete current-
  filter selection across server pages and explicit one-probe-per-device Ping
  outcomes.
- The host timeout policy now classifies `devicecleanup/exportWorkbook` as a
  batch action as well as the frontend correlation policy. A regression test
  prevents the export from falling back to the nine-second default lifetime.

## Next

1. Finish targeted AD computer discovery and remaining legacy candidate/route
   fixes; complete the working-set and source-profile compatibility checks.
2. Verify every G2 workflow before switching C2 navigation, then run full
   regression/security/desktop checks and finish documentation (phase 9).
3. Keep company/tenant/remote-client acceptance explicitly unverified until run.
   No tag, release or installer publication without explicit approval.
