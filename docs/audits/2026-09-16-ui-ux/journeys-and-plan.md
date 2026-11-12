# Before/after journeys and actionable plan

Before: `184a10f1a521032db06c63e9d0df1eff3ddda1a9`. After: `664bbe4604801a19b88724ac3beaf0ac48cc0ecc`. See [evidence](evidence.md) for provenance, observations and limits; [findings](findings.md) for issue details.

## Comparison method

Count an intentional navigation/action activation as one action. Text entry, scroll distance, waiting, source authentication, network latency and preparation are reported separately, not converted into invented click totals. “Primary entry” starts on another application page with the desktop rail available. Populated comparisons assume the same necessary evidence is already loaded/stored. Cold-session differences are called out explicitly.

These are structural minimums from route/render code, with selected live examples. They are not timing experiments. A safe identity-resolution step may be necessary; an unnecessary wrapper or lost filter is not equivalent to that step.

| Task and starting condition | Before | After | Assessment |
| --- | --- | --- | --- |
| Inspect stored operating state of an unambiguous WEC subject; required results present | Clients → row → operational Overview: **2** navigations. | Devices → native WEC link → Windows overview link: **3**. Via operational Clients link: **4**. | +1 / +2 for the old summary, C02. Identity correctness improved; summary placement regressed. |
| See posture/finding filters and prepare a batch from primary navigation | Clients: **1** to workbench/filter view. | Devices → Client posture…: **2**. | +1 and an additional choice. No batch operation executed; preparation only. O02/O09, C03. |
| Open Health for an already selected exact device | Overview → Health: **1**. | Canonical Overview → direct Health link: **1**. Using Health & security → Open Windows Health: **2**. | Do not claim an unavoidable extra click. Two competing paths and a shell change are the problem. C02. |
| Open detailed Event Logs for selected exact device | Event logs tab: **1**. | Event logs section: **1**, adapter routes to exact Windows shell. | Tool retained and minimum count equal. Section vocabulary/layout changes. Query still requires its explicit Run action. C02/C11. |
| Find an unloaded AD account/computer through Ctrl+K | Enter query; bounded directory results; select match. | Enter query; no matching loaded object; leave search; choose/load appropriate source and query; select matching reference. | Automatic query removed intentionally, but no explicit continuation replaces it. No fixed extra count because scope/cache/setup differ. C06. |
| Enter department/OU-filtered AD user work from primary navigation | Users: **1**, filters present; apply query. | Users → AD query workspace: **2**, filters present; apply query. | +1; capability retained. Current canonical list alone cannot express the same AD query. O10/C03/C14. |
| Find a cloud account from connected M365 source with no loaded inventory | Source Users → explicit bounded source read, then loaded search. | Source Users → canonical list → expand source loader → explicit bounded read, then loaded search. | New loading disclosure/changed destination adds setup. Observed 760 accounts after explicit read. O04. Neither version proves complete tenant inventory. |
| Inspect several filtered cloud accounts | Old source table has inline name/UPN; old list return behavior not controlled live. | `admin` + Entra → reference → Users root resets to 760; history Back restores 30. | Concrete current context defect, O05–O07. No fabricated before timing or blanket claim that old Users preserved all filters. |
| Inspect AD direct groups from an account with a DN observation only | AD access list with direct-group observations; no equivalent general scoped-group profile. | Groups section → exact-DN resolver → explicit resolve → scoped group → explicit member load. | New useful capability. Some deliberate actions establish safe identity/coverage, not regressions. Prefer eliminating repeated lists, not bypassing resolution. C09. |
| Find one member in a loaded Entra group | Member CloudTable: loaded search, sort, page; name links inline. | Group profile: long members list, then separate Direct member profiles list; no cloud member search/sort/page. | Real control loss at the canonical destination; source code only, C09/C12. |
| Device → associated user → related device | Fragmented source views/context panels. | Exact typed scoped links, separate candidate evidence and all Intune enrollments. | Improvement in semantic reliability and navigation breadth. Return-to-origin still needs S1. Actual multi-hop live journey not repeated here. |
| Inspect a Nessus candidate's findings | Specialist asset/source key or legacy address matching. | Exact source-record/profile finding link with source key. | Preserve: clearer provenance, no accidental attribution of another namesake's risk. C16 and retained specialist mapping. |
| Open Cleanup from another page | Device Cleanup rail: **1**. | Devices → Device Cleanup: **2**. | +1 via rail, but direct global-search/finding/old URL entry remains. |
| Open comparison from another page | Clients → Compare: **2**. | Devices → Compare: **2**. | No navigation-depth regression for this shortcut. Stored-data comparison still preserved. |
| Open AD domain/DC analysis from primary navigation | Active Directory: **1**. | Data sources → Active Directory: **2**. | +1; old Overview card/direct route can still reach it directly. Inconsistent placement, not deletion. |
| Work with packages | Patch Management: **1**, one header/tab row. | Software & licenses: **1**, two headers/navigation rows. | Same route depth but greater visual overhead and label ambiguity. O08. |
| Cancel active management read | Clients Cancel load: **1**. | Generic Cancel source read: **1** but ineffective for management; dedicated Cancel then required. | Control correctness regression, independently observed. O03 versus O09. |

## Improvements that must survive remediation

| Improvement | Why it helps administrators | Preservation gate |
| --- | --- | --- |
| Source + authority + native ID in references | Prevents confusing same-named computers/accounts and opening the wrong execution subject. | Same-name/different-domain and duplicate-ID fixtures never select another record or Windows target. |
| Separate candidates and confirmed relations | Keeps uncertain matching useful without presenting invented ownership/compliance. | Candidate values remain attributable to their own source; unknown does not become healthy/disabled. |
| Entra-native accounts, native groups, all Intune enrollments | Makes cloud-only investigation possible without manufacturing an AD counterpart or collapsing enrollments. | Cloud-only profile remains usable; direct/associated/registered semantics remain explicit. |
| Full working-set filter/sort before pagination | Searching a loaded subset behaves predictably across its pages. | A match on a later loaded page is found; source totals and configured limits stay distinct. |
| No external reads on canonical profile/search keystroke | Prevents unintended scans/crawls and keeps reads auditable and bounded. | Keyboard typing, canonical section changes, history return and opening cached summaries perform no new provider scan. Explicit source-query actions are separate. The retained Windows shell's BitLocker exception F16 must be corrected rather than declared compliant. |
| Cached evidence with availability/freshness/retention | Distinguishes absent evidence, failed read, retained stale facts and partial coverage. | Failed refresh keeps old facts with old times and clear error; expiry suppresses invalid facts. |
| Exact legacy routes and specialist modules | Preserves valuable mature workflows and external bookmarks. | G2 aliases, exports, batch cancellation, comparison, Leaver and existing package/print gates remain covered. |
| License-to-loaded-account and typed object links | Reduces manual copying of IDs between unrelated source pages. | Tenant/SKU/native ID carried exactly; no export/permission expansion. |
| Existing structured errors and responsive shell | Provides understandable recovery and smaller-window navigation. | Keep cause/next-action/retry and drawer/focus behavior; improve affected pages using existing components. |

## Alternatives and ADR implications

| Option | Benefit | Cost / decision |
| --- | --- | --- |
| **Recommended: keep scoped objects; restore operational first-level views and summaries** | Small targeted changes; retains correctness improvements and specialist modules. Users can start by task or object. | Requires product choice for default device mode and column priority. Fits ADR 0022's selected navigation and no-new-framework boundary. |
| Restore Clients as a separate primary item alongside Devices | Immediate access to old fleet work, small routing change. | Exposes two competing device lists; changes ADR 0022 item 1. Use only if task testing shows named modes within Devices do not suffice. Requires explicit ADR amendment before implementation. |
| Restore AD analysis as a primary operational item | Makes domain/DC/hygiene work discoverable again. | Also changes ADR 0022 sidebar decision. A direct named shortcut under existing structure is the smaller first experiment. |
| Fully revert consolidation | Removes new wrapper friction quickly. | Loses source-native groups/accounts, safer identifiers/candidate handling and stronger relationship navigation; broader regression risk. Not supported by findings. |
| Build generic entity/grid/profile/navigation infrastructure | Can centralize future behavior. | Disproportionate to concrete defects; adds maintenance and migration risk. Rejected for this audit. |

The audit recommends rolling back **specific presentation decisions**: the identity-only device landing content, technical three-column defaults, duplicate wrappers and loss of cloud-member table controls. It does not recommend reverting source identities or weakening execution checks. No ADR was edited.

## Prioritized implementation packages

Implementation requires a separate start instruction. Each package is intentionally small enough for its own review and acceptance. Do not include unrelated provider or framework work. Sequence: **S1 → S2 → S3 → S4 → S5 → S6 → S7 → S8**. S1's pre-existing specialist follow-up may follow its canonical fix rather than delaying S2.

### S1 — Use the real originating result context

**Addresses:** F03; then the confirmed portions of F14.

**Changes:** add Back to results to actual scoped device/account/group profiles. Carry origin through typed relationship navigation and the exact Windows view. Consume existing route/history state; retain separately labelled root navigation. Handle direct bookmarks with a safe default list. Then fix individual specialist origins which demonstrably lose state.

**Acceptance:**

1. Using actual routed components, filter Users to Entra + a query on page 2, choose size/sort, scroll and focus a result; open profile, follow one group/device link, return. Query, source/tenant, page, size, sort, scroll and focused row are restored.
2. Equivalent Devices posture/saved-target origin survives canonical profile → Windows Health → return. No credential is copied into navigation state.
3. From empty-history deep links, return is safe and visible. It does not navigate outside WEC or replay a stale unrelated origin.
4. Replace/extend the test-only Profile navigation fixture with a test using the actual header control. Alt+Left continues to work. No source scan is triggered by return.

### S2 — Correct cancellation and unintended live reads

**Addresses:** F05/F16. Keep the cancellation and BitLocker changes as two small reviewable substeps.

**Changes:** bind Cancel to the real current operation owner. Display one clear name/status and one effective cancel, with available source progress; retain existing provider cancellation/generation mechanisms. Then make the existing live BitLocker read an explicit action rather than a hidden SnapshotGrid mount effect.

**Acceptance:**

1. Start a deferred environment refresh from Devices; click the displayed Cancel once. `environment.cancel` is invoked and the running state ends consistently. A late result does not update the cancelled working-set revision.
2. Repeat for AD and Entra reads; their bridge cancellation still fires once.
3. Read/cancel/retry stays usable; no two confusing Cancel controls, no re-enabled conflicting operation while the original is still represented as active.
4. Live smoke uses existing bounded read scope only. Do not require remote probes or log payload inspection.
5. Mount the real exact Windows shell with cached Inventory and Overview selected: the disk-encryption reader is not called. Selecting a tab alone does not start it. The explicit encryption check invokes the exact target once and displays its own status/time; no new storage is required.

### S3 — Put operating-state information back in device Overview

**Addresses:** F01 and the two-shell part of F02.

**Changes:** reuse existing stored device/OS, Health, software, security and observed-user summary content in the canonical Overview. For cloud-only records, show the loaded account/device management facts useful to that subject, with clear source. Keep provenance/candidates available underneath and exact-target action requirements unchanged.

**Acceptance:**

1. From Devices, one reference selection shows the stored OS, latest Health outcome/time, inventory/software age and relevant source exceptions without opening another overview. Missing data shows Not collected/Unavailable, never fabricated zeros or Healthy.
2. Health and Event Logs are each reachable in one further action for an exact eligible Windows target. Return preserves the object and origin.
3. A cloud-only device shows useful loaded source facts; Windows tools explain the missing exact target. No name-based automatic target or compliance attribution occurs.
4. Use already existing DTOs/sections; no new query permission, collection, persistence or generic profile system. Test mixed stale/missing sources and exact-address namesakes.

### S4 — Restore task entry and explicit source discovery

**Addresses:** F02/F04 and part of F13.

**Changes:** expose Operational view and Source records as clear first-level choices inside Devices; make AD query a visible account/group mode. Carry entered global query into an explicit bounded AD-source action. Reuse the selected configured directory context; do not demand retyping it unnecessarily. Keep Compare/Cleanup and AD analysis shortcuts directly named.

**Acceptance:**

1. Posture/finding filters and the existing workbench are reachable from primary Devices without finding a prose link to another nominally separate inventory. No additional wrapper header is added.
2. A known AD object outside the loaded first page can be found using a deliberate source-search action from the entered query. The selected scope, maximum results, progress and cancel are clear.
3. Typing alone makes zero external query calls. Partial results say how to narrow/continue; no entire-domain/tenant crawl is introduced.
4. Department/OU user queries, all original posture filters, saved targets, Compare, Cleanup and old bookmarks still work. Baseline the click paths in the table above and record actual acceptance actions.
5. If separate top-level Clients or AD navigation is selected instead, obtain a product decision and amend ADR 0022 in that implementation change before switching the sidebar.

### S5 — Make object lists useful for scanning

**Addresses:** F06/F07.

**Changes:** clickable human name plus UPN/address; task-relevant source-specific columns; compact source/age/status; full IDs and normal provenance in disclosure. Keep conflicts and actionable failures visible. Put result count in the current object-kind/filter context. Collapse infrequent filters into an explicit More filters control while showing active filters and Clear filters.

**Acceptance:**

1. At the same 1602 × 1040 outer-window size used here, a standard single-source account list with no expanded detail shows at least **eight complete rows** without scrolling. This is a proposed acceptance target, not a measured baseline. At smaller windows, controls remain usable with contained table scrolling.
2. Two equal display names can be distinguished by UPN/address and source without opening profiles. Clicking the name still routes to the exact native reference.
3. User list shows UPN/account state; Intune view exposes associated user, compliance and last sync; Entra device view exposes OS/trust/last sign-in where returned. Do not flatten independent source state into one inferred value.
4. Load only users, switch to Devices: device total and current match count are unambiguous. A collapsed source area still names failed/stale/partial source exceptions; details show original retrieval/attempt times and limits.
5. Filtering still precedes pagination. Clear filters preserves intended tenant/source boundaries explicitly. No persistent cloud index or additional data collection.

### S6 — Restore member browsing and useful relationship navigation

**Addresses:** F09 and profile repetition in F06/F07.

**Changes:** use one bounded member list/table for facts and typed inline links. Restore loaded search/sort/page for cloud members. Align the AD loaded-page filter with the same navigable rows. Move routine source IDs/metadata beneath useful identity facts; keep relation semantics visible.

**Acceptance:**

1. For a synthetic 120-member returned cloud set, search finds a later loaded member, sort is stable and paging works. Source count/limits remain distinct from filtered count.
2. On an AD member page, filter to one account: exactly its row/link is shown, without another unfiltered link list. Loading the next source page is explicitly distinguished from paging/filtering loaded results.
3. Unknown/limited-information members remain visible; unsupported types do not route as users. Nested group links open that group only, never recursively expand it.
4. Following member/user/device links and returning passes S1; cloud associated/registered/direct semantics and AD authority remain unchanged.

### S7 — Remove duplicate navigation and standardize interaction

**Addresses:** F08/F10/F13; native narrow-window verification gap.

**Changes:** one Software header/navigation, consistent Packages/Client versions/History terms, clear source-connection versus AD-analysis links. Use one intentional profile section pattern across canonical and Windows content. Reuse the existing responsive shell.

**Acceptance:**

1. Software presents exactly one navigation row for its five destinations. Old `/patchmanagement?section=...` bookmarks land on equivalent content.
2. Overview shortcuts and sidebar use a documented consistent destination vocabulary. Health, Security, Event Logs and application Error log remain distinct.
3. Keyboard-only: open search with Ctrl+K, choose a result with arrows/Enter, traverse sections, return and close overlays with Escape. Focus stays visible and returns sensibly.
4. Independently check populated lists and profiles at 1602 × 1040, 1280 × 800 and approximately 800 × 650 outer sizes; drawer scroll/close/focus, long IDs, wrapping sections and table overflow remain usable. Record actual achieved dimensions and limits; do not substitute CSS tests for the native check.

### S8 — Repair the bounded pre-existing inspection gaps

**Addresses:** F11/F12/F15 and confirmed remaining F14 cases.

**Changes:** explicit selectable Event Log message detail; refresh/indicate new stored Overview evidence after a completed capture; distinguish initial stored-read failures from missing data; repair the remaining proven specialist return paths. Keep these changes separate from identity and source collection. A simple loaded-software name/version/publisher filter is a separate small follow-up; do not expand this into a new inventory table system or Health history store.

**Acceptance:**

1. A long synthetic Event Log message is fully readable/selectable using keyboard, closes predictably and returns focus to the same row. No extra external query or export is necessary to inspect it.
2. Successful Inventory/Security/Health capture makes updated stored summary timestamps/results visible on returning to Overview, or supplies an explicit new-results control. A failed capture does not erase older evidence or relabel it fresh.
3. Reporting readiness still refreshes correctly and exports retain their existing schema and privacy boundary.
4. Action Center and AD query return to their original query/filter/page when using their visible return controls. Add focused real-route tests only for those confirmed journeys.
5. For Inventory, Security and Health, a genuine missing result renders the empty view; a rejected stored-read request renders a clear read error with Retry stored read. That retry invokes no live scan.

## Completion gate for the remediation effort

For each package, retain the exact build/commit, record its relevant native journey and run focused component/route tests plus the normal required checks for changed code. At the final milestone, run the full frontend suite/build, backend/contracts checks when affected, and the G2 compatibility matrix. Re-run the specific fixed scenario after each relevant change; do not claim a general UX pass from test totals alone.

The first reviewable implementation result should therefore be **S1: the actual Back to results control with a failing-then-passing real-profile regression test**. S2 follows with the management cancellation test. These two bounded changes can be accepted independently before deciding the larger first-level Devices presentation in S3/S4.
