# UI/UX remediation — implementation and acceptance

Date: 2026-09-28
Integrated branch: `master`
Base: `664bbe4604801a19b88724ac3beaf0ac48cc0ecc`
Automated baseline: `20557cb`

## Outcome

S1-S8 and the code corrections for F01-F16 are implemented without changing
the accepted architecture, permissions, persistence boundaries or read-only
policy. Source-native identities, candidates, exact-target requirements and
explicit bounded reads remain intact. No provider, directory write, implicit
scan, persistent identity registry, export expansion or frontend framework was
added.

The automated acceptance baseline is green and the self-contained Release
WinForms/WebView2 application was exercised visually and interactively against
the installed profile. The pass covered populated provider data, native window
sizes from 1602×1040 down to 800×649, keyboard navigation, dialogs and the two
stateful return journeys. Two defects found only in the native run were fixed:
Event Log actions no longer collapse into character-wide columns at narrow
widths, and closing a Device Cleanup review no longer discards the originating
Action Center return state.

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
| S8 | F11, F12, F14, F15 | Event messages open in a keyboard-accessible selectable dialog, Escape closes it and focus returns to the trigger. Successful Inventory/Security/Health captures invalidate stored Overview and Reporting summaries. Missing results remain empty states; rejected stored reads show a retry that performs no scan. Action Center and AD user detail journeys retain URL-addressed filter/page/sort context and expose visible safe return controls. The native follow-up keeps Event Log columns usable at 800 px and preserves Action Center navigation state while a cleanup review is opened and closed. | `edb4dc4`, `20557cb` |

## Finding disposition

| Findings | Disposition |
| --- | --- |
| F01-F05 | Implemented and covered by focused component/real-route tests. |
| F06-F09 | Implemented and covered with duplicate-name/source-field, count/error and bounded-member fixtures. Native 1602×1040 evidence shows more than eight complete AD rows and two equal display names distinguished by UPN, source and separate-candidate warnings. |
| F10-F16 | Implemented and covered by keyboard focus, legacy-route, long-message, refresh, return-cycle, stored-error and no-hidden-read regressions. Native keyboard, narrow-window, dialog and populated return-cycle confirmation passed. |

## Final automated gates

| Gate | Result |
| --- | --- |
| `git diff --check` | Pass |
| `dotnet build --configuration Release` | Pass; 0 warnings, 0 errors |
| `dotnet test --configuration Release` | Pass; 1,059 tests |
| Contract generator `--check` | Pass; 501 generated types current |
| `npm test -- --run` | Pass; 630 tests in 104 files |
| `npm run build` | Pass; initial JS 312.43 kB, 98.93 kB gzip |
| `npm audit --audit-level=high` | Pass; no High/Critical issue. Two existing Moderate Vitest/@vitest-mocker advisories remain; available remediation is a breaking major update. |
| G2 preservation | Pass at automated level through the full route, specialist, export, settings, operation and compatibility suites. No G2 route/function was removed. |
| Isolated Release-host startup | Pass; migrations, local assets and WebView2 initialized, startup bridge reads succeeded, no Error/Fatal log entry |
| Supervised frontend viewport/input check | Pass at 1602×1040, 1280×800 and 800×650 for the Devices source/operational hierarchy, responsive shell, Ctrl+K, Escape and drawer focus restoration. This used the actual routed React UI with an isolated in-memory bridge stub; it is not native WebView2 acceptance or populated-provider evidence. |
| Native viewport/input acceptance | Pass in the self-contained Release host at 1602×1040, 1281×799 and 800×649, with an additional populated inspection at 1920×1140. Details are recorded below. |

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
cancellation still stops the sequence.

The user's post-fix native screenshot from a locally started Debug host confirms
the complete explicit sequence at 2026-09-17 16:34: users 761, licenses 10,
groups 3,023, Entra devices 1,143 and Intune devices 100. Every card is cached,
none is partial, and the matching host log records the organization read plus
all five inventory reads as successful followed by the final status refresh;
there is no Microsoft 365 Error or Fatal entry. This remains the native
provider-backed evidence for the source behavior; the separate self-contained
Release viewport and keyboard checks are recorded below.

## Native visual and interactive acceptance — 2026-09-28

The published self-contained `win-x64` host was used directly; no browser mock
or shell-driven UI automation substituted for WebView2. The following was live
observed:

1. At 1602×1040, an explicit bounded AD read loaded 100 users. The account
   table showed more than eight complete rows with clickable account name, UPN,
   operational facts and Active Directory source. Two `Allgaier, Isabell`
   records remained separate and were visibly distinguished as
   `AllgaierMA@kauth.local` and `AllgaierKF@kauth.local`, including the explicit
   candidate warning.
2. At 1281×799 and 800×649, the navigation drawer remained scrollable. Escape
   closed it and restored a visible focus ring to the opener. Profile tabs
   wrapped without page overflow. Event Log data retained readable columns and
   used horizontal table overflow; the full-message action stayed on one line
   after the native defect fix.
3. Ctrl+K opened focused global search. Typing `PK-CLNB126`, ArrowDown and Enter
   opened the device profile. ArrowRight moved between profile tabs with a
   visible focus ring. Escape closed global search and the Event Log detail
   dialog; the dialog restored focus to its triggering action.
4. `PK-CLNB126` System errors for the last 24 hours returned nine native event
   rows. Event 20 opened with complete selectable content and a usable dialog at
   800×649. No remote write, implicit scan or export was started.
5. Action Center search `opsi` produced the URL-addressed filter state
   `/actions?q=opsi&page=1&size=25&sort=SEVERITY&dir=ASCENDING`. Opening and
   closing the first Device Cleanup review retained the visible return control;
   returning restored both that URL and the `opsi` input value.
6. The AD query workspace searched `Allgaier`, opened the scoped
   `AllgaierKF@kauth.local` profile and returned through the visible control.
   The original query, page, page size and sort URL were restored, together
   with all three same-name results.
7. The earlier provider-backed Microsoft 365 evidence remains valid: the
   explicit read returned users 761, licenses 10, groups 3,023, Entra devices
   1,143 and Intune devices 100, with every card cached and none partial.

The narrow Event Log layout and cleanup-return regressions discovered during
this pass are covered by focused component tests in addition to the repeated
native journeys. No unresolved visual or interactive acceptance item remains
for S1-S8.

## Workspace and release boundary

The completed remediation and subsequent corrections were fast-forwarded into
the local `master` checkout. Temporary implementation branches were removed;
only `master` remains locally. At this baseline `master` is 97 commits ahead of
`origin/master`. No push, tag, installer publication, GitHub Release or
published release asset change was performed. The local `publish` directory
contains the verified self-contained executable and is not a release
publication.
