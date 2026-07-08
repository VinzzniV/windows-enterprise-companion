# TODO — Backlog from the 2026-07-02 project review

Living document: check items off, reprioritize freely, delete what gets
rejected. Ordering within a section is by value/effort. Milestone work
(M6, M7) still follows the loop protocol: ADR + user decisions first.

## Done 2026-07-06 — Client-centric IA + Saved Targets + Print overhaul (ADR 0010)

Approved plan "Voller Umbau", shipped slice by slice (Strangler, not big-bang):
- **Phase 0:** fixed the Inventory local-machine duplicate (learn the machine
  name from `system/getAppInfo`, dedupe on restore); new
  `Wec.Modules.Targets` saved-targets module (host + role + user name, never a
  password) with migration and integration tests; shared `TargetProvider`
  React context (saved targets + per-host session credentials in memory only).
- **Phase 1:** Clients workspace — AD-sourced list (search + OS/site grouping,
  unpopulated until opened), per-client detail with on-demand
  Inventory/Security/Diagnostics/Printers/Reporting sections reusing the
  existing views, and a two-client inventory/security compare.
- **Phase 2:** navigation split into **Clients** (primary) + **Fleet** +
  **Multi-host** (standalone pages kept as batch runners).
- **Phase 3:** Print page reworked — queues merged per physical device
  (serial → IP → base name), compact toner mini-bar, search, site grouping;
  client-installed printers as a separate CIM path (`scanClientPrinters`).
- **Phase 4:** `SavedTargetsBar` in the Print/AD/Patch pickers, pre-filled by
  role.

Backend build + all tests green; frontend build + tests green (86).

## Done 2026-07-03 — UI/UX rework (foundation, slices 1–6)

Approved plan "Neu gedacht" (dark + fresh accent, dashboard). Shipped as
independent slices: (1) bundled Inter + JetBrains Mono fonts and Tailwind
v4 semantic tokens (accent indigo + ok/warn/fail/info); (2) shared form
primitives (Input/Select/Checkbox/Field/Toolbar) eliminating the three
divergent input styles; (3) DataTable upgrade (zebra, sticky header,
per-column align/mono, accessible row-click) and a dot+label Badge system;
(4) app-shell rework (grouped nav, accent active state, global top bar with
privilege + breadcrumb); (5) accent adopted on buttons/focus/selection and
a full token color sweep across all pages, AD states unified; (6) Dashboard
landing page with module tiles derived from persisted data. Frontend
build + 54 tests green.

Follow-ups spawned by that work:

- [ ] Shared `ResultContext` component — the Security/Diagnostics context
      boxes already match; Inventory's master/detail header differs slightly.
- [ ] Optional: command palette (Ctrl+K) for navigation + quick actions
      (slice 7, not built).
- [ ] Live visual pass on the real host once the rework is running (the
      session that built it could not screenshot the WinForms/WebView2 app).

## Done 2026-07-03 — Patch management (opsi) MVP

New Wec.Modules.PatchManagement + "Patch Management" page (ADR 0008):
IOpsiClient Core seam with a JSON-RPC opsiconfd client (typed errors incl.
untrusted-CA hint), session-only in-memory credentials with test
connection, dashboard joining opsi depots/clients/products/states with WEC
inventory software via IInstalledSoftwareInventoryProvider and a manual
mapping table (exact-match suggestions only), depot filter with Denkingen
default, full workflow-state model (MVP derives Detected/UpdateAvailable/
RolloutRequested/Completed/Failed), mandatory rollout preview + explicit
confirmation before the only write (actionRequest=setup), "prepare
packages" as a planned+audited opsi-package-updater command, persistent
audit log with UI.

Follow-ups spawned by that work:

- [ ] Execute opsi-package-updater over SSH (needs an SSH client
      dependency decision + own ADR revision); today the command is only
      planned and audited
- [ ] Nessus/vulnerability data per opsi productId (criticality column is
      prepared conceptually via the mapping table, ADR 0008)
- [ ] Pilot/test client group flow (Ready for pilot → Approved states are
      modeled but not yet driven by the UI; current admin acts as pilot)
- [ ] Verify against the real opsi server (JSON-RPC shapes are tested
      against fixtures; live opsi 4.2/4.3 field variance not yet proven)

## Planned — Automated package/version currency check (decided 2026-07-03)

opsi only compares client ↔ depot; whether the *depot package* itself is
outdated (vs. uib repo or vendor) is checked by nobody. Plan (researched
against the opsi 4.3 docs, discussed 2026-07-03):

- [ ] **A — Own opsi package repository** (server-side config, prerequisite):
      HTTP(S)-served directory with the self-built `.opsi` files (naming
      `product_<prodVer>-<pkgVer>.opsi` is the version metadata) + a `.repo`
      file in `/etc/opsi/package-updater.repos.d/` (`baseURL`, `dirs`,
      `active = true`; template `example.repo.template`). Makes
      `opsi-package-updater list --updatable-packages` the single truth for
      uib **and** self-built packages; multiple depots pull from the same
      repo (or repo type `opsiDepotId` to replicate between depots).
- [ ] **B — WEC reads the repos over HTTP** (no SSH needed): parse the
      `.opsi` file names from the repo directory listings (uib:
      opsipackages.43.opsi.org/stable, plus the own repo), compare against
      productOnDepot, surface the reserved `DownloadNeeded` workflow state
      in the dashboard ("repo has 128.0-2, depot has 127.0-1"). Repo URLs
      as WEC options. Executing the update stays the planned/audited
      opsi-package-updater command (no stable JSON-RPC for it; SSH is the
      known follow-up).
- [ ] **C — Vendor-level check via winget** (second step, for self-built
      packages without a repo source): `winget show --exact --id <Id>` as
      version oracle, opsi productId ↔ winget id via a mapping table like
      the software mapping; needs version normalization, not every internal
      tool exists in winget.

## Done 2026-07-03 — Print management module (ADR 0009)

New Wec.Modules.PrintManagement + "Print Management" page: print-server
inventory over root\StandardCimv2 (queues, shares, drivers + versions,
ports with device IPs) enriched per device over a new minimal SNMP v2c
read-only client behind the ISnmpReader Core seam (serial, model,
sysName/sysLocation, status, page count, RFC 3805 toner levels with
low-threshold flag); unreachable devices are visible per-printer errors,
never a scan abort. Snapshot store WITH history + serial-based lease diff
(new/gone/swapped-at-queue, unread serials counted), CSV export
(semicolon-separated) via the save dialog, device web-UI links through
the shell, consistency hints (orphaned queues, driver version spread,
missing location/comment, default `public` community). Community string
is configuration, never logged.

Follow-ups spawned by that work:

- [ ] Verify SNMP values against the real Utax fleet (standard Printer-MIB
      is fixture-tested; firmware variance proven on first live scan)
- [ ] Backlog: central toner notification (WEC monitors levels, mails the
      service provider, audit entry = the "when was it ordered" trace;
      needs SMTP + scheduled scans + own ADR) — only if watching the
      overview stops being enough
- [ ] Backlog: per-queue defaults (duplex/color) via
      MSFT_PrinterConfiguration if a use case appears (one method call per
      queue)

## Done 2026-07-03 — Remote completion pass

Inventory: plausible link speeds (WMI sentinel → unknown), per-adapter
IPv4/IPv6 addresses, connected-first ordering with collapsed disconnected
adapters; persistent per-host snapshots with delete and restore-on-load;
parallel multi-host scans bounded by MaxParallelScans; remote software
inventory via StdRegProv (IWmiQueryService.InvokeMethodAsync) with
structured errors instead of silently empty lists. Diagnostics: optional
target, WMI-based checks remote-capable (domain membership, services, disk
space via Win32_LogicalDisk, update recency, reboot pending via StdRegProv),
connectivity probes visibly skipped as local-perspective, shared
Local/Remote/Multiple scope flow with per-host summaries. AD: credential
normalization (UPN, DOMAIN\user, credential domain, directory-domain
fallback) and an activedirectory/testConnection bind check.

## Done 2026-07-03 — Enterprise UX pass

Shared design-system primitives (Button, PageHeader, SummaryMetric,
EmptyState/ErrorState, DetailsDisclosure, DataTable, EvidenceList) and a
consistent information architecture across all five pages: severity/status
summary strips, host/status/timestamp context on every result view,
LOCAL-ONLY / NOT-RUN checks rendered as coverage notes instead of findings,
compact batch rows, grouped inventory cards, collapsible diagnostics
evidence, AD overview/hygiene sections with action-oriented error hints,
explicit local-only scope on Reporting.

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

- [ ] Remote path for the registry-based *security* checks (RDP, Secure
      Boot, UAC) via StdRegProv — the IWmiQueryService method-invoke
      extension exists now (used by remote software inventory and the
      reboot-pending diagnostic); the security checks still report
      LOCAL-ONLY
- [ ] Batch-scan cancellation from the UI (bridge needs a cancel channel)
- [ ] Multi-host executive summary report (Reporting reads local data only)
- [ ] Verify remote scans against a real second machine/test domain (error
      mapping is unit-tested; WSMan HRESULT paths not yet proven live)

## P2 — Quick wins on existing data

- [ ] **AD section in the executive summary report** — decided 2026-07-02:
      explicit "include live AD analysis" checkbox in the export UI (visible
      run, no silent queries, no new persistence); report renders overview +
      hygiene sections when checked
- [ ] **TypeScript type generator** for `api-types.ts` — decided 2026-07-02:
      small C#→TS generator tool run as build step, CI fails on diff
      (kills the manual-sync drift class)

## P3 — Mid-term features

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
