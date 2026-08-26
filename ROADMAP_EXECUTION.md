# Windows Enterprise Companion — Roadmap Execution

Status: `IN_PROGRESS`

Current phase: Phase 0 — Stabilize the delivery baseline

Current slice: Run the complete Phase 0 verification gates

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

## Blocked

- None.

## Deferred release gates

- AD, Kaspersky, opsi and Nessus live validation in the company environment.
- Remote Inventory, Health, Event Log, Ping and WinRM smoke tests against a
  designated non-critical client.
- Explicit approval for version tag, installer publication and GitHub Release.

## Last verification

- Baseline commit: `aba4ccd57f724cb359e9ac643378bf6ada0ce559`.
- Local `HEAD` equals `origin/master`; ahead/behind `0/0`.
- Last verified Release build: 0 warnings, 0 errors.
- Last verified backend tests: 701 passed.
- Last verified frontend tests: 415 passed.
- Last verified bridge contracts: 308 generated types current.
- Last verified frontend production bundle: 535.98 kB JavaScript,
  155.62 kB gzip.
- Current known NPM audit state: four High-Severity findings with patch-level
  fixes available.
- GitHub CLI is authenticated; Actions storage currently contains 84 artifacts
  using approximately 5.27 GiB.
- Local Inno Setup compiler is unavailable; installer verification relies on
  GitHub CI.
- `%APPDATA%\Wec\usersettings.json` is not present on this host.

## Next

1. Run the complete Phase 0 verification gates.
2. Push the verified milestone, run CI and manually exercise the packaging
   workflow without publishing a release.
