# WEC UI and UX audit — 16 September 2026

Status: audit complete; implementation not started. Documents describe the local consolidation branch at the commit below. They do not change the accepted architecture or supersede the existing acceptance reports.

## Decision summary

The reported deterioration is substantially supported. The strongest evidence concerns task access, return navigation and the prominence of technical evidence. It does **not** support reverting the scoped identity model or claiming that most administration functions have been deleted.

1. Restore an operational device overview and a first-class way to reach posture, saved targets and batch work. A device profile should answer what is known about this device before explaining its record composition (F01–F02).
2. Repair the visible return path. In the live session, `Users → search admin + source Entra → account → Users` changed 30 filtered results into 760 unfiltered results. Alt+Left restored the original search correctly (F03).
3. Give an empty or incomplete search an explicit, bounded source-search action. Searching a loaded subset is useful, but is an inadequate replacement for finding an account or computer that has not been loaded (F04).
4. Fix the management cancellation control. The generic Cancel action leaves the management read running; a second, differently named Cancel does stop the UI's management operation (F05).
5. Reduce list and profile overhead, restore task identifiers such as UPN, remove duplicate software navigation, and make actionable freshness warnings visible without exposing every technical field (F06–F09).

No P0 data-corruption or unauthorized-write defect was established by this audit. Several P1 daily-work regressions and P2 interaction weaknesses warrant correction before calling the consolidation UX-equivalent.

The audit also found older issues that must not be blamed on consolidation: initial stored-read failures can appear as no data (F15), and the exact Windows shell can start a hidden live BitLocker read when cached Inventory mounts (F16). The latter conflicts with the stored-only opening boundary and belongs with the early read-control fixes.

## Read the audit

- [Inventory](inventory.md): all 32 registered routes, workspaces, profile sections, specialist functions, dialogs and cross-links; includes changed and legacy entry points.
- [Findings](findings.md): reproducible problems with priorities, origin classification, evidence and impact.
- [Before/after journeys and implementation plan](journeys-and-plan.md): explicit starting conditions, interaction differences, improvements to retain, eight small acceptance-gated work packages.
- [Evidence and limitations](evidence.md): immutable Git references, independent live observations, code locations, checks and unverified cases.
- [Source UI manifest](source-ui-manifest.csv): 985 declaration sites from 108 production TSX files, including controls, headings, tables, navigation and role-bearing elements. This is a source completeness annex, **not** 985 unique functions or 985 live-tested controls.

## Baselines and preservation

| Role | Git reference | Reason |
| --- | --- | --- |
| Existing checkout, left unchanged | `master`, `e89395fe874d64aa09ad185185df804390025e95` | This is the user's current checkout. It does not contain the completed consolidation. Initial working tree was clean. |
| Audited consolidation | `codex/object-centered-consolidation`, `664bbe4604801a19b88724ac3beaf0ac48cc0ecc` | Latest locally available consolidation tree; built and run from an isolated archive. |
| Before consolidation | `184a10f1a521032db06c63e9d0df1eff3ddda1a9` | The recorded consolidation start baseline. It already contains Microsoft 365, avoiding a misleading comparison against a much older product. Its application tree matches its application predecessor `8e3640d`. |

Both versions were extracted with Git archive into temporary directories, built there, and launched with separate named runtime profiles. No checkout switch, source edit, migration of the user's normal database, commit, push or ADR amendment was performed. Only this audit directory was added to the repository.

The current Release host and frontend build succeeded; all 559 frontend tests in 97 files passed. The before-version frontend and Release host also built successfully. Green tests do not establish task usability: one existing return-navigation test supplies a substitute profile with a Back button that the real profile does not offer.

## Product recommendation

Keep Devices, Users and Groups as useful destinations, with source-native accounts, groups and explicit candidates. Change what is shown first and how work is continued. Restore compact operational summaries, human-readable entry links and direct access to specialist work. Do not introduce a universal entity UI, persistent CMDB, new permissions, automatic scans, implicit target selection or another frontend framework.

The recommended first package is the visible return path (S1), followed immediately by the cancellation fix (S2) and the device overview (S3). These provide small, independently verifiable improvements. S4 then restores task entry and source search. Any decision to reinstate separate top-level Clients or Active Directory navigation requires an explicit amendment to ADR 0022; this audit proposes alternatives and makes no such amendment.
