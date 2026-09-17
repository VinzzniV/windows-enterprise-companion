# UI/UX remediation — implementation and acceptance

Date: 2026-09-17
Integrated branch: `master`
Base: `664bbe4604801a19b88724ac3beaf0ac48cc0ecc`
Automated baseline: `7ab29b2f6271e6c004c82fd0ab68cc2dd26fd88a`

## Outcome

S1-S8 and the code corrections for F01-F16 are implemented without changing
the accepted architecture, permissions, persistence boundaries or read-only
policy. Source-native identities, candidates, exact-target requirements and
explicit bounded reads remain intact. No provider, directory write, implicit
scan, persistent identity registry, export expansion or frontend framework was
added.

The automated acceptance baseline is green. A new native WinForms/WebView2
viewport run is still required before describing the remediation as fully
accepted. The available Computer Use provider exposed browser control only. On
the current host it returned `apps: []`, and its documented native `getApp` and
`listApps` functions were unavailable. This was not replaced with PowerShell UI
automation.

The final self-contained `win-x64` publish was started from the integrated
checkout against the normal installed profile. The host applied migrations in
789 ms, loaded the packaged local assets and initialized WebView2 runtime
153.0.4234.32. Startup bridge reads completed successfully and the log had no
Error or Fatal entry. The pre-existing EF multiple-collection query warning
appeared on the cached Security overview read. The exact published executable
remained running while Computer Use again returned `apps: []`, so no target
window, screenshot, dimension or input action could be obtained.

## Package evidence

| Package | Findings | Result | Reviewable commits |
| --- | --- | --- | --- |
| S1 | F03, F14 foundation | Real scoped profile headers consume an allowlisted originating working set. Query, source/tenant, sort, page, size, scroll and focus survive profile and relationship journeys; direct bookmarks use safe roots and navigation state excludes credentials. | `632b341` |
| S2 | F05, F16 | Management cancellation calls the current operation owner once and ignores late completion. Cached Inventory no longer starts BitLocker on mount; one explicit check performs the live read and owns its status. | `90ccd84`, `286970a` |
| S3 | F01, F02 profile split | Canonical WEC Overview reuses stored Inventory, software, Health, Security and user evidence with original provenance. Cloud-only records show only loaded native facts; Windows tools require an exact eligible target. | `55f19c2` |
| S4 | F02, F04, F13 | Devices exposes Operational view and Source records as the only first-level device views. Compare/Cleanup/AD analysis remain directly named as subordinate related tasks. The duplicate Devices/Users/Groups row is no longer rendered on Devices. A global query still carries into a deliberate bounded AD continuation and typing performs no external query. | `299a33f`, `e094cf2` |
| S5 | F06, F07 | Object lists lead with clickable names and UPN/address, source-specific operational fields, current-kind counts and named source exceptions. Technical IDs/provenance and infrequent filters move behind disclosures without flattening source state. A follow-up makes the 5,000-record bound independent per object kind and restricts coverage warnings to sources relevant to the current kind. | `1aa6f00`, `a7f0c44`, `8f24e06` |
| S6 | F09 | AD and Entra members use one navigable bounded table with loaded search, stable sort and local paging. AD source paging remains distinct; limited/unknown types stay visible and unsupported types are not routed as users. | `76fdbb7` |
| S7 | F08, F10, F13 | Software has one five-destination navigation and compatible legacy bookmarks; destination vocabulary is aligned. The operational device page now uses the consolidated Devices title. Canonical Device/User sections use the same roving ArrowLeft/ArrowRight tab behavior as retained detail shells, and the narrow navigation drawer restores focus only after the inert main region is released. | `7a127a6`, `bb7be2e`, `4a970f2`, `e094cf2` |
| S8 | F11, F12, F14, F15 | Event messages open in a keyboard-accessible selectable dialog, Escape closes it and focus returns to the trigger. Successful Inventory/Security/Health captures invalidate stored Overview and Reporting summaries. Missing results remain empty states; rejected stored reads show a retry that performs no scan. Action Center and AD user detail journeys retain URL-addressed filter/page/sort context and expose visible safe return controls. | `edb4dc4` |

## Finding disposition

| Findings | Disposition |
| --- | --- |
| F01-F05 | Implemented and covered by focused component/real-route tests. |
| F06-F09 | Implemented and covered with duplicate-name/source-field, count/error and bounded-member fixtures. The proposed eight-row and narrow overflow targets still require the native viewport run. |
| F10-F16 | Implemented and covered by keyboard focus, legacy-route, long-message, refresh, return-cycle, stored-error and no-hidden-read regressions. Native end-to-end keyboard and viewport confirmation remains pending. |

## Final automated gates

| Gate | Result |
| --- | --- |
| `git diff --check` | Pass |
| `dotnet build --configuration Release` | Pass; 0 warnings, 0 errors |
| `dotnet test --configuration Release` | Pass; 1,059 tests |
| Contract generator `--check` | Pass; 501 generated types current |
| `npm test -- --run` | Pass; 629 tests in 104 files |
| `npm run build` | Pass; initial JS 312.43 kB, 98.93 kB gzip |
| `npm audit --audit-level=high` | Pass; no High/Critical issue. Two existing Moderate Vitest/@vitest-mocker advisories remain; available remediation is a breaking major update. |
| G2 preservation | Pass at automated level through the full route, specialist, export, settings, operation and compatibility suites. No G2 route/function was removed. |
| Isolated Release-host startup | Pass; migrations, local assets and WebView2 initialized, startup bridge reads succeeded, no Error/Fatal log entry |
| Supervised frontend viewport/input check | Pass at 1602×1040, 1280×800 and 800×650 for the Devices source/operational hierarchy, responsive shell, Ctrl+K, Escape and drawer focus restoration. This used the actual routed React UI with an isolated in-memory bridge stub; it is not native WebView2 acceptance or populated-provider evidence. |
| Native viewport/input acceptance | Blocked by unavailable native Computer Use; no visual or input pass claimed |

The first full Release build exposed two culture-sensitive timestamp parses in
the new Microsoft 365 object-list test. Commit `a7f0c44` made both comparisons
use `InvariantCulture`; the complete Release build then passed. The first full
frontend run exposed one obsolete test query after profile controls changed
from generic buttons to tabs. Commit `4a970f2` updated that real multi-hop
journey; the repeated complete frontend run passed.

## Follow-up visual acceptance — 2026-09-17

The primary sidebar was compared against consolidation base `664bbe4` and is
unchanged: `routeRegistry.tsx` still exposes the grouped Work, Objects,
Operations and Administration navigation. The regression was introduced by
this remediation branch in `299a33f`, which rendered Operational view, Source
records, Compare, Device Cleanup and AD analysis with equal visual weight and
left a second Devices/Users/Groups row directly below them.

Commit `e094cf2` restores the intended hierarchy without rolling back S4:
Operational view and Source records are the two device views; Compare, Device
Cleanup and AD analysis remain visible as related tasks. The operational page
heading is Devices rather than the legacy Clients label. At 1602×1040,
1280×800 and 800×650 the two levels remained distinct and wrapped without
horizontal page overflow. Ctrl+K opened focused global search; Escape closed
search and the narrow drawer. The live browser check exposed that the drawer
previously attempted to focus its opener while the main region was still
`inert`; focus restoration now runs after the render and was observed on the
Open navigation button.

This check rendered the actual application routes and production styles in a
local browser with deterministic empty bridge data. It verifies frontend
layout and input behavior only. Native window chrome, WebView2 accessibility,
populated lists and provider-backed journeys remain in the native checklist
below. A real isolated Release host was started again, but Computer Use still
returned no native apps and `cua.getApp` remained unavailable.

The user's populated native screenshots then exposed a separate bounded-source
defect: Graph declared 3,023 groups but the Microsoft 365 collection stopped at
2,000, and two later shared limits reduced the Groups working set to 1,225.
Commit `8f24e06` raises the explicit Graph collection bound to 5,000 and applies
the session object-list and frontend working-set bounds independently per object
kind. Groups coverage no longer reports unrelated Nessus device failures. Unit,
component, full-suite, build and contract checks cover the corrected semantics.
A post-fix populated native screenshot now shows 3,023 loaded groups, 3,023
current matches and 5,174 source records across all object kinds without the
previous unrelated Nessus exception. The same retest exposed that the combined
Microsoft 365 action did not request the already-supported license inventory;
the follow-up adds `LICENSES` to that explicit sequence. Individual failures no
longer suppress later source reads or the final status refresh, while explicit
cancellation still stops the sequence. A post-fix native license result remains
to be observed.

## Native acceptance still required

Use the running self-contained Release host from the integrated checkout.
Record actual outer-window dimensions and verify:

1. At 1602×1040, a populated single-source account list shows at least eight
   complete rows; name, UPN/address and source distinguish equal names.
2. At 1280×800 and approximately 800×650, the drawer scrolls, closes with
   Escape and restores focus; long native IDs, wrapped profile tabs and table
   overflow remain usable.
3. Keyboard-only Ctrl+K → arrows/Enter → profile section arrows → visible
   return works; Escape closes search and the Event Log message dialog with
   sensible focus restoration.
4. Re-run the visible Action Center and AD-query return journeys at a populated
   state and confirm their original query/filter/page controls.
5. Sign in to Microsoft 365, run the explicit core inventory read, and confirm
   that the 3,023-group tenant result is no longer marked partial or reduced by
   another object kind's rows. The Groups page may still report a genuine AD
   group-read failure, but must not report Nessus device-read failures.

Prior native consolidation acceptance remains valid historical evidence, but
it is not substituted for this required post-remediation viewport run.

## Workspace and release boundary

The completed remediation and subsequent corrections were fast-forwarded into
the local `master` checkout. Temporary implementation branches were removed;
only `master` remains locally. At this baseline `master` is 90 commits ahead of
`origin/master`. No push, tag, installer publication, GitHub Release or
published release asset change was performed. The local `publish` directory
contains the verified self-contained executable and is not a release
publication.
