# Scoped object navigation and working set

`workingSet.ts` captures a disposable, minimal list/search snapshot. It shares
the configured record bound across source reads, preserves each source's
coverage/counts/errors and clones the captured rows. Source updates therefore
do not mutate an already displayed page. `queryWorkingSet` filters, sorts and
pages that same snapshot; its total is exact only for the working set.

Scoped native IDs are grouped before filtering and paging. AD/Entra user links
require an explicit selected authority pair, unambiguous account SIDs and a
complete applicable Entra collection or bounded exact SID proof. Entra/Intune
registration links require complete applicable directory evidence and retain
every enrollment reference. A source/index bound, unknown matching ID or
conflicting identifier prevents automatic joining. Names only expose candidates;
WEC addresses remain weak asset identities. Source-specific filters never inherit
another source's enabled state or license assignment.

`workingSetSources.ts` maps concrete source projections into these list fields.
Source totals are never added into an environment-wide total. Native record IDs,
unknown states/assignments, original observation times and query provenance
remain available. Nothing in these helpers performs a source request, writes a
cache to browser storage, constructs a full object profile or selects an action
target from a candidate.

`WorkingSetProvider` owns the displayed and available revisions in React memory.
Configured bounds cover both rows and source-read metadata. Cache-only refreshes
read concrete M365 and local storage projections; targeted local searches remain
bounded and separate. AD source buttons add explicitly requested pages. No typing
event performs a directory or Graph request. Cloud disconnect/tenant changes,
administrative credential changes and cache expiry invalidate affected evidence
and profile hooks. Ordinary source revisions require explicit adoption so a page
does not move during investigation.

`ObjectWorkingSetPage` and global search consume the same captured revision.
Source, nullable account state, separate AD/Entra device states, Intune compliance
and management, identity conflicts/candidates, saved/scanned evidence, OS, SKU,
text, ordering and page selection apply
before paging. URL state restores filters and paging through profile navigation;
bounded in-memory positions restore list scroll/focus. Canonical routes are
`/devices`, `/users` and `/groups`; the initial `/users/workspace` and
`/groups/workspace` aliases remain valid. Devices links to posture/batch scans,
Compare and Cleanup. AD query workspaces remain under `/users/directory` and
`/groups/directory`. Software & licenses and Data sources reuse specialist
workflows; old routes remain functional and highlighted in their parent section.

The device profile has a compact overview and seven addressable areas. Existing
Windows tools retain exact execution targets; cloud-only profiles show why those
tools are unavailable. Intune compliance remains separate from Windows Security.
Account SKU links retain the tenant and selected SKU in the license catalogue.

Raw management cache reads contribute original AD/KSC/opsi/Nessus observations.
`managementRecordRoutes.ts` uses a disposable workspace/snapshot/source/index
locator when a scoped native ID is missing. Duplicate source names remain separate;
the same observation repeated by a targeted cached query is deduplicated without
claiming physical identity. `ManagementRecordPage` exposes all original fields,
source uncertainty and candidate navigation without a Windows execution target.
Its Nessus findings link retains the exact stored asset key. Source refresh replaces
old locator queries in the available revision; a connection change immediately
removes the displayed evidence. The random snapshot ID prevents cross-run reuse.
