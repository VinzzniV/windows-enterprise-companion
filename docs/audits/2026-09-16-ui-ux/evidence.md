# Evidence, method and limits

## Evidence rules

- **O — observed:** exercised or visually inspected in a running native WinForms/WebView2 application during this audit. Observations describe the displayed state, not an independently verified backend transaction trace.
- **C — code-proven:** a concrete render, route or state path exists in the audited source. Live frequency, latency and user impact are not inferred as measurements.
- **T — tested:** automated checks independently executed here, or a specifically identified test inspected. An inspected test is not automatically an end-to-end proof.
- **H — hypothesis:** plausible impact requiring another scenario or representative users. Not counted as an established regression.
- **Prior:** information from earlier acceptance reports, clearly separate from this audit's observations.

Priorities express task impact: P1 obstructs frequent identification, continuation or control of work; P2 adds material friction or weakens interpretation; P3 is polish or a preference requiring validation. Regression means a demonstrable change against the selected before version. Newly added capabilities can have weaknesses without being regressions from an equivalent earlier capability.

## Authority and history

Read root `AGENTS.md`, `ROADMAP_DECISIONS.md`, `ROADMAP_EXECUTION.md`, `docs/ultimate-admin-tool-roadmap.md`, `docs/architecture-and-m1-plan.md`, the relevant ADRs through 0023, `docs/consolidation-analysis-and-plan.md`, `docs/consolidation-verification.md`, `docs/consolidation-live-acceptance.md` and the earlier UI audit. Superseded ADRs 0014–0016 were interpreted through accepted ADR 0017; their removed pilot, rollout and manufacturer-regex features are not consolidation regressions.

Important constraints: ADR 0022 requires G2 feature/deep-link preservation before sidebar replacement; source-native identity and uncertainty; explicit bounded reads; no implicit scans, crawls or target inference; no cloud export expansion. Plan C3 calls for useful summary information with detail underneath; C7 calls for return context. ADR 0018 retains detailed Event Logs and historical Health/Diagnostics. ADR 0019 retains AD authority and read-only Leaver work. ADRs 0001–0007 keep execution and credential boundaries. No conflict was resolved by changing an ADR.

| History landmark | Meaning |
| --- | --- |
| `184a10f` | Before baseline recorded by the consolidation program. |
| `6db92be` | Consolidation authorization/documentation. |
| `498ccb5` | Shared loaded working set and search. |
| `70649fd` | Sidebar changes to the new object destinations. |
| `1731d60` | Specialist workspace composition. |
| `1f8db4e` | Seven device profile areas. |
| `585bfeb` | AD multi-key sorting correction; do not report earlier acceptance F-01 as still open. |
| `951b3de` | Interrupted Nessus synchronization correction; do not report earlier F-02 as still open. |
| `8d982ff`, `664bbe4` | Later acceptance documentation and test corrections; audited endpoint is `664bbe4`. |

The checkout is `master` at `e89395fe874d64aa09ad185185df804390025e95`; the audited target is `664bbe4604801a19b88724ac3beaf0ac48cc0ecc`, and before is `184a10f1a521032db06c63e9d0df1eff3ddda1a9`. Source locations below refer to the audited target unless explicitly marked Before. They may not exist at the checkout's HEAD. Reproduce a reference without switching branches:

```powershell
git show 664bbe4604801a19b88724ac3beaf0ac48cc0ecc:frontend/src/features/clients/DeviceProfilePage.tsx
git diff 184a10f1a521032db06c63e9d0df1eff3ddda1a9 664bbe4604801a19b88724ac3beaf0ac48cc0ecc -- frontend/src/app/GlobalSearch.tsx
```

## Independent native observation log

The two applications ran from `%TEMP%/wec-ui-ux-audit-20260916/current` and `before`. Runtime profiles were `ui-ux-audit-20260916` and `ui-ux-audit-before-20260916`. No pre-existing WEC window was available at the beginning. The existing executable in the checkout did not identify the consolidation version, so it was not used as a proxy for that version.

Window captures were inspected in the tool session, not written into the repository. Coordinates below are approximate positions in the 1602 × 1040 outer-window capture, not CSS pixels or a cross-device performance benchmark. Company identifiers, raw records, log contents, account details and screenshots containing live data are intentionally absent from the audit artifacts. Counts describe the bounded session, never the whole organization.

| ID | Reproduction / observation | Outcome and limits |
| --- | --- | --- |
| O01 | Open current Overview and inspect grouped sidebar. | 13 sidebar destinations. Consistent dark visual language, readable primary headings, separate scrollable rail. Existing overview cards still use Clients, Patch Management, Active Directory and Report export while the rail uses Devices, Software & licenses, Data sources and Reports. |
| O02 | Open Devices in the fresh named runtime profile. | Empty loaded set, large coverage card, collapsed source loader, nine always-visible query/filter controls. Source connection does not itself make the list populated. The result area is near the bottom of the captured window. |
| O03 | Expand source loader; start Read configured management sources; click Cancel source read; then Cancel management source read. | Two Cancel controls appear. Generic Cancel clears its own busy UI but the management read remains active with its dedicated Cancel still present. Dedicated Cancel restores the management read control. No provider logs were inspected; the confirmed claim is UI/state behavior plus C05. |
| O04 | Data sources → Microsoft 365. User personally signs in; open Users; expand source loader and explicitly read Entra inventory. | Login succeeded. Users initially opens a source-filtered empty list; the separate explicit read loads 760 accounts. No login automation, consent change or credential extraction. |
| O05 | Inspect loaded Users at top, loader collapsed. Search `admin`. | Table heading near y754; two complete rows plus part of a third visible. Rows repeat source display name, tenant identifier and native ID; display name is not the entry link. UPN is absent from the list. Local search responds and shows 30 matches, with Entra retained. |
| O06 | Open first filtered account. | Profile's first viewport contains source references and an AD source-resolution form ahead of Entra account facts. Identity, Groups, Devices, Licenses, Activity and Leaver review buttons are visible. The UPN is in the profile, below substantial technical content. No additional relationship query was executed here. |
| O07 | Click profile's visible Users link. Then use Alt+Left twice. | Visible link resets search and source to defaults: 760 matches. First history Back reopens the profile; second restores `admin`, Entra and 30 matches. History is functional; the visible return link bypasses it. Deep-page scroll/focus restoration was not independently proven in this live run. |
| O08 | Open Software & licenses → Packages. | Two headings and two navigation rows. Outer Packages/Client versions/Licenses/Winget packages/History; inner Overview/Clients/Winget packages/History. The first package rows are low in the viewport. Source read-only overview loads; no package action executed. |
| O09 | Build/run Before; open Clients. | Posture/source/group filters, comparison, page connectivity and batch work are visible directly. The page automatically starts environment source loading, showing per-source state, elapsed time and partial progress; cancelled deliberately. This baseline also has substantial space overhead above results. No remote connectivity or batch scan executed. |
| O10 | Before → Users. | Directory connection defaults to this machine's domain; name/account/UPN/employee-ID, department, OU and account controls are directly available with Apply filters. Initial directory inventory failed with a structured cause/next-action/retry view. Cause was not diagnosed, so no successful before account timing/density comparison is claimed. |
| O11 | Attempt native resize through edge drag and system Size menu. | A narrower stable capture could not be obtained; later foreground activation returned tool errors. This is a test-harness limitation, not an application defect. Responsive code and prior acceptance were reviewed; no new live narrow-window pass or failure is claimed. |

The before Clients source load returned progress from existing configured providers and was cancelled during the remaining source load. These were bounded existing read paths. Host-specific remote probes, Health scans, Event Log queries, printer checks, network scans, exports and administration writes were not executed. We did not inspect credential stores, user configuration contents, authentication tokens or operational logs.

## Code evidence index

All paths below start at `frontend/src/`. The CSV annex supplies further declaration line numbers. Line numbers refer to the immutable target above; named symbols are the stable lookup anchors.

| ID | Source | What it proves |
| --- | --- | --- |
| C01 | `app/routeRegistry.tsx:89` (`appRoutes`) and Before equivalent | 32 routes / 13 sidebar entries versus 18 routes / 14 sidebar entries; canonical and compatibility destinations. |
| C02 | `features/clients/DeviceProfilePage.tsx:15`, `:74`, `:97`, `:108`; `features/clients/sections/OverviewSection.tsx:119`, `:147`, `:177`, `:212`, `:248` | Seven new sections; exact-target redirect; Overview identity/evidence content; existing richer OS, Health, software, security and user summary. |
| C03 | `shared/objects/ObjectWorkingSetPage.tsx:21`, `:45`, `:66`, `:103`; `WorkingSetSourceControls.tsx:71` | URL filters, per-history-key scroll/focus map, source/ID-only entry links, list fields and specialist links; separate source query and loaded search. |
| C04 | `features/users/ScopedUserProfilePage.tsx` header/section renderer; `features/groups/GroupProfilePage.tsx:29`; `features/clients/DeviceProfilePage.tsx:81`; `features/clients/ManagementRecordPage.tsx` | Root links ignore originating list state in the three normal profiles. Management record has a narrower `fromWorkingSet` return implementation. |
| C05 | `shared/objects/WorkingSetSourceControls.tsx:28`, `:35`, `:87`, `:96` | Generic `cancel` does not call `environment.cancel`; `readManagement` uses the environment instead of `active`. Dedicated management cancel calls both. |
| C06 | `app/GlobalSearch.tsx:28`, `:37`; Before `app/GlobalSearch.tsx`; `app/workingSetSearch.ts`, `app/searchResults.ts` | Current local set search, six per object kind; no explicit source-search continuation. Before performs bounded directory searches. Keyboard arrow/Enter/Escape support remains. |
| C07 | `shared/objects/WorkingSetCoverage.tsx:7`, `:21`, `:25`; `shared/objects/SourceReadState.tsx:9`; `workingSet.ts:270` | Coverage component is independent of current object kind/filter; stale/errors are in disclosure, raw state detail is verbose; underlying unknown/stale/retention semantics are retained. |
| C08 | `features/patchmanagement/SoftwareLicensesPage.tsx:6`; `PatchManagementPage.tsx` section renderer; `features/verwaltung/DataSourcesPage.tsx` | Nested wrappers retain complete specialist headers/tab bars. No new write authority. |
| C09 | `features/groups/GroupProfilePage.tsx:62`, `:82`, `:90`; `shared/objects/ObjectRelationships.tsx`; `features/users/ScopedUserProfilePage.tsx` access section | Member facts and profile links are separate lists. AD local filter affects facts, not the following relationship list. General groups and typed links are new functionality. |
| C10 | `features/clients/ClientDetailPage.tsx:26` and tab renderer; `features/users/UserDetailPage.tsx` versus new profile section buttons | Old tablist/roving/arrow behavior versus regular button navigation in new profiles. Still Tab-accessible, not entirely inaccessible. |
| C11 | `features/clients/sections/EventLogSection.tsx:45`; `OverviewSection.tsx`; `features/clients/ClientDetailPage.tsx` report revision and completion callbacks; Before equivalents | Hover-only full Event Log message and stored-overview refresh boundary predate consolidation. Report invalidation is separate. |
| C12 | `features/microsoft365/cloudObjectDestination.ts:4`; `Microsoft365Page.tsx`; `LicenseWorkspace.tsx` | Resource aliases map to scoped objects, source-filtered lists and SKU context; old list presentations are bypassed on normal canonical navigation, their underlying facts remain in profile renderers. |
| C13 | `app/App.tsx` `TopBar`, `ApplicationShell`; `shared/ui/DataTable.tsx` | Responsive rail/drawer, wrapping header, main scroll area, maximum content width, table scroll and keyboard paths. Layout mechanisms are not proof of every narrow populated view. |
| C14 | `features/actioncenter/ActionCenterPage.tsx`; `features/users/UsersPage.tsx`; Before equivalents | These specialist pages retain much of their filter state in component state. Loss across route unmount is not a newly introduced general problem. |
| C15 | `shared/objects/ObjectWorkingSetPage.test.tsx:18` (`Profile`, `view`) | History test renders a test-only profile containing `navigate(-1)`; it does not exercise the actual root return link. |
| C16 | `features/clients/ClientEntryPage.tsx:10`; `shared/objects/objectRoutes.ts`; `managementRecordRoutes.ts` | Exact address/scoped identity adaptation; management snapshot locators; safe invalid/expired references and legacy compatibility. |
| C17 | `features/clients/sections/InventorySection.tsx:37`; `features/clients/sections/HealthSection.tsx:25`; `features/clients/sections/SecuritySection.tsx:34`; Before equivalents | Initial stored-read failures are mapped to idle/empty states regardless of error category. |
| C18 | `features/inventory/InventorySnapshot.tsx:64` (`EncryptionCard`); `features/clients/ClientDetailPage.tsx:198`; `features/clients/sections/InventorySection.tsx:101`; backend `src/Modules/Wec.Modules.Inventory/Application/DiskEncryptionService.cs:23` | Hidden mounted Inventory can render cached SnapshotGrid and invoke a live BitLocker status reader without an explicit check. Same before-version render/read mechanism. |
| C19 | `features/inventory/InventorySnapshot.tsx:298`; `features/clients/sections/HealthSection.tsx`; backend `src/Modules/Wec.Modules.Diagnostics/Persistence/EfDiagnosticRunRepository.cs:51` | Installed software has a disclosure and scrollable table but no local search; Health shows latest only and its store replaces per-host results. Do not mistake retained legacy contracts for a history UI. |

## Independently run checks

| Check | Result |
| --- | --- |
| Initial Git status | Clean on the original checkout. |
| Current locked frontend install | `npm ci --no-audit --no-fund` succeeded in the temporary current tree. |
| Current frontend suite | `npm test -- --run` with two workers: **559 tests in 97 files passed**, about 144 seconds. Node localStorage warnings and an isolated test-router warning appeared; no test failed. |
| Current production frontend | `npm run build` succeeded, 198 transformed modules. |
| Current Release host | Build succeeded, zero warnings/errors, including rebuild after assets. |
| Before frontend and Release host | Production frontend succeeded (170 modules); Release host succeeded with zero warnings/errors, rebuilt after frontend assets. |
| Backend tests, contracts/security/package audit | Reviewed prior evidence and relevant tests/code; **not independently rerun** as part of this documentation audit. Historical full backend counts are not represented as this audit's result. |
| Final artifact / checkout verification | Six new audit files only; tracked checkout unchanged. `git diff --check` passed. Additional direct checks of the new files found no trailing whitespace, inconsistent Markdown table columns, broken local document links or raw GUIDs. |

No tests or application code were added. Existing tests validate many correctness boundaries: full-working-set filtering before pagination, exact scoped links, name candidates, cache generation and tenant cancellation, explicit source reads, legacy aliases and gated specialist operations. Their passing results do not validate visual hierarchy, real return controls or the management cancellation wiring identified here.

## Unverified scenarios and next validation

- Narrow populated lists, drawer focus cycle, screen reader narration, 200% scaling, very wide monitors and complete keyboard-only task completion need fresh native checks. Prior live acceptance reports a 733 × 654 pass; that is prior evidence only.
- Current real group/member, Intune relationship, local Windows Health/Event Log and export-completion journeys were code-reviewed but not completed live here. Earlier acceptance successes must not be read as independent retests.
- Freshness after retention expiry, failed refresh retaining old rows, conflicting AD/Entra states, duplicate IDs, large mixed-source working sets and cross-tenant switching were not manufactured in the live company session. Existing automated fixtures provide partial coverage.
- Source latency and success vary with connection, authentication, permissions and loaded scope. No controlled task-time experiment or representative-admin study was run. Click deltas in the journey document are structural lower bounds with stated preconditions.
- No claim of a comprehensive accessibility conformance audit, WCAG contrast measurement, complete company inventory or successful administration-write validation is made.

For subsequent acceptance, use synthetic duplicate names and long addresses plus a documented authorized local target. Measure the actual UI at the same window sizes and starting caches, exercise real routed profiles, and record counts/outcomes without copying live identifiers.
