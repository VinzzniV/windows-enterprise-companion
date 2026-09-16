# Prioritized UX findings

Use [evidence.md](evidence.md) for O/C/T definitions, source paths and immutable commits. Journeys and acceptance packages are in [journeys-and-plan.md](journeys-and-plan.md). The priorities below are task-based judgments, not measured user-study scores. No P0 defect was established.

| ID | Priority | Finding | Evidence | Origin |
| --- | --- | --- | --- | --- |
| F01 | P1 | Canonical device Overview no longer answers the operational question | C02 | Regression in the default profile |
| F02 | P1 | Main device entry hides posture and operational work behind another workspace | O02/O09, C01/C03 | Reachability regression |
| F03 | P1 | Visible profile return discards the originating result context | O05–O07, C03/C04/C15 | New profile defect; partial regression, older Users had similar weakness |
| F04 | P1 | Global search cannot continue to a not-yet-loaded source object | C06, O02/O04 | Deliberate behavior change with unresolved task regression |
| F05 | P1 | Generic Cancel does not cancel the management read it appears to control | O03, C05 | New control regression |
| F16 | P1 | Opening the exact Windows shell can trigger a hidden live BitLocker read | C18 | Pre-existing behavior conflicting with the stored-only opening boundary |
| F06 | P2 | Technical metadata displaces useful identifiers and result rows | O05/O06, C03/C12 | New list presentation regression; some overhead pre-existed |
| F07 | P2 | Current-view counts and actionable source freshness are difficult to interpret | O02/O05, C07 | New working-set weakness |
| F08 | P2 | Software has duplicate navigation and two page identities | O08, C08 | Wrapper regression |
| F09 | P2 | Group members lose collection controls and separate facts from navigation | C09/C12 | Cloud regression plus new AD-profile weakness |
| F10 | P2 | New profile section controls have different keyboard behavior | C10 | Interaction regression |
| F11 | P2 | Event Log messages require hover for their full content | C11 | Pre-existing weakness |
| F12 | P2 | Successful capture can leave the stored Overview showing an older state | C11 | Pre-existing refresh-boundary weakness |
| F13 | P2 | Labels and source placement make specialist tools harder to locate | O01/O08, C01/C08 | Mixed regression and existing terminology debt |
| F14 | P2 | Specialist filter context is still lost across some detail journeys | C14 | Pre-existing weakness, not evidence against identity consolidation itself |
| F15 | P2 | Stored-read failures are presented as no previous capture | C17 | Pre-existing trust/empty-state weakness |

## F01 — Device Overview is an identity preface rather than a work overview

**Affected view:** `/devices/:source/:scope/:objectId?section=overview`.

**Reproduce from source:** choose a valid WEC reference with stored Inventory/Health/Security data. Read the canonical Overview renderer. It displays source/scope/ID, Windows tool links, relationship/candidate counts and source freshness metadata. It does not render the existing OS/CPU/memory/disks, software, Health and security summary cards. Select “Windows overview, saved target and PowerShell” to reach those cards in the older shell.

**Before:** a normal `/clients/:host` entry displayed `OverviewSection`, already organized around device/OS, Health, software, security, linked users and management status. **After:** the compatibility adapter routes normal host entries to the identity-centered profile; a further link is needed for that overview. Cloud profile fields likewise require Identity & management rather than being summarized in Overview.

**Impact:** the first page answers “which source record is this?” more readily than “what is wrong and what do I inspect next?” This is a content/reachability regression even though the underlying evidence remains available. Device identification must remain explicit, but long identity explanations are not a substitute for the existing operating-state summary.

**Evidence:** C02/C16, direct comparison of before/current render paths. **Not live-verified:** a populated current WEC profile or time-to-diagnosis; do not interpret the old acceptance's successful Health scan as a retest here.

**Correction:** S3, reuse the existing stored summary in the canonical view, scoped to the exact subject. Keep candidate states independent. No new collection or new composite “overall health” score.

## F02 — Devices is no longer the first place to do device work

**Affected view:** `/devices`; secondary `/clients`.

**Flow:** open Devices to find machines with a posture/finding, compare, work with saved targets or run an approved batch. The default has source/account/compliance/identity filters. Posture, findings, grouping and batch selection live under “Client posture, saved targets and batch scans.” In a fresh session, the primary page may be empty despite connected sources; the source-loading disclosure is a separate interaction.

**Before:** Clients directly exposed the operational filters/workbench and loaded the bounded management environment, with per-source progress. **After:** the same tool exists behind one additional navigation choice. Comparison remains directly linked from Devices, so its navigation depth has not necessarily increased. Cleanup changes from one sidebar click to Devices plus Cleanup (unless global search or a finding link is used).

**Impact:** two device inventories with different questions and filters are presented as one canonical object workspace plus a subordinate old workspace. An administrator must know the implementation split to choose the right starting place.

**Evidence:** O02 versus O09; C01/C03. The old Clients page also has excessive pre-table overhead; this finding does not assert its visual layout was ideal or that automatic environment loading should be restored wholesale.

**Correction:** S4. Make Operational view / Source records explicit first-level modes within Devices. Preserve bounded source state and target restrictions. A complete top-level Clients rollback would conflict with ADR 0022's selected sidebar and needs a decision; first-level operational mode can preserve the chosen sidebar.

## F03 — The visible return path bypasses working-set restoration

**Affected views:** canonical account/device/group profile header links to their root lists.

**Observed flow:** Users loaded from Entra → search `admin` (30 matches) → first native profile link → visible Users link. Search is cleared and source becomes All loaded sources, with 760 matches. Alt+Left reopens the profile; a second Alt+Left restores the exact filtered list.

**Mechanism:** list links send `fromWorkingSet` and the list saves scroll/focus by history key. Real profile headers use plain `/users`, `/devices`, `/groups` links. The state needed for recovery exists but is not consumed. Relationship links also drop the original list context, making multi-hop work harder to resume.

**Impact:** repeat query/filter setup while checking multiple results, or discover an undocumented keyboard workaround. This scales with the number of objects inspected, not the size of a single click penalty.

**Origin qualification:** the old AD Users page already held filters locally and could lose them across an unmount. Thus the live account example proves a current defect and failed consolidation return criterion, not that all user-return behavior became worse. The old operational Clients flow deliberately carried view state; the new profile/Windows transition no longer consistently carries that state, establishing a device-path regression from code. ManagementRecord is a partial exception: it uses the original URL, though a fresh history key does not itself restore the saved scroll/focus.

**Test gap:** `ObjectWorkingSetPage.test.tsx` renders a fake `Profile` with its own Back to results `navigate(-1)`. Passing this test validates the list's history behavior, not the real profile's return control.

**Correction:** S1. A visible, real Back to results control must restore the originating query, scope, page, size, sort, scroll and focused result. Preserve root navigation as a separately named option. Cold deep links need a safe root fallback.

## F04 — Global search is a loaded-subset filter without a discovery continuation

**Affected view:** Ctrl+K/global search; fresh object lists.

**Flow:** search for a known AD computer/account which has not been loaded into the current working set. No matching object is returned. The administrator must leave search, choose a workspace, open its source loader, supply the directory scope/query or choose the old AD query workspace, perform a bounded read, then search again.

**Before:** global search performed bounded directory computer and account queries. **After:** it consults the displayed working set and navigation/saved targets only. The visible placeholder and empty-state wording are honest about loaded data; this reduces false promises but does not finish the user's task.

**Impact:** an entry point named global search no longer finds an available source object in an initially unloaded session. Repeated provider setup and two kinds of query increase work. Cloud lists were already bounded before; do not describe prior cloud search as a complete tenant search.

**Evidence:** C06; O02/O04 establish the initial-state setup burden, not a live current Ctrl+K source lookup experiment.

**Keep:** no network activity per search keystroke, one stable displayed revision, bounded scope. **Correction:** S4 adds “Search selected AD source for …” as an explicit action with the current query carried forward. ADR 0022 permits explicit bounded queries; reintroducing automatic search-time crawls would violate it.

## F05 — Two Cancel buttons control different operations without making that clear

**Affected view:** Devices → Load a source into this working set.

**Observed flow:** Read configured management sources → both Cancel source read and Cancel management source read appear → generic Cancel source read disappears when clicked, but the management operation remains active → dedicated Cancel management source read stops that UI operation.

**Mechanism:** `readManagement` delegates to `environment.refresh()`. Generic `cancel()` only cancels `active.current`, increments a generation and clears local `busy`. That reference is used for directory/cloud bridge calls, not management refresh. Only the dedicated button invokes `environment.cancel()`.

**Impact:** the visible cancellation acknowledgement is misleading; other source controls become usable while the management operation continues. The audit did not inspect backend logs and does not claim how far each provider request progressed after cancellation.

**Before:** Clients had one environment Cancel control and source-by-source progress/elapsed feedback. **Correction:** S2, one named active operation and one cancellation path bound to the actual owner. Test the deferred environment refresh, not just a directory bridge mock. Do not add a generic job framework.

## F06 — Lists prioritize provenance over recognition and comparison

**Affected views:** three working-set lists, especially Users; cloud account Identity.

**Observed example:** at 1602 × 1040 outer size, Users with the loader collapsed shows about two complete rows. Its table starts around y754. Each row repeats the display name, tenant/native identifiers and identity prose. The bold human name is not clickable. UPN appears only after opening the profile; there the source-reference and AD-resolution cards precede the Entra identity facts.

**Before:** the cloud user table exposed a linked name, UPN, enabled state, department and license count as columns. The Intune list exposed associated user, last sync and compliance; the Entra device list exposed trust, OS version and last sign-in. Those first-level fields moved to detail. The earlier Clients page already had substantial empty-state/filter/workbench overhead, so not all density problems started here.

**Impact:** slower discrimination between similarly named accounts and more profile openings to answer simple questions. GUIDs are important inspection data, but their repeated prominence does not help most list selection.

**Evidence:** O05/O06, C03/C12 and Before `Microsoft365DataView`. No claim of a measured percentage slowdown or successful populated-before viewport comparison.

**Correction:** S5: linked name + task identifier, relevant status columns, compact source badge/age, exceptional conflicts prominent, full reference detail on demand. Retain accurate source-specific states and exact navigation identifiers.

## F07 — Coverage is available but not aligned to the immediate decision

**Affected views:** shared Loaded working set card and source-profile status blocks.

**Code-proven flow:** load accounts and open Devices. `WorkingSetCoverage` has no object-kind/filter input and displays the shared snapshot's total objects, while the table below filters for Devices. The label “displayed objects/addresses” therefore does not describe the current table. This exact cross-kind example was not completed live; the global card and filtered match count were separately observed in Users.

**Freshness flow:** per-source last-attempt errors and stale state in working-set lists are mainly under a generic coverage/timestamps/errors disclosure. The closed disclosure count is the number of reads, not the count of failed sources. A reader cannot identify whether an actionable source failed from that number alone. Meanwhile profiles print several lines of retrieval/attempt/coverage metadata repeatedly.

**Impact:** too much generic technical explanation and too little compact decision context. Risk of interpreting a mixed working-set total as the current list total, or overlooking a retained stale source. Actual misinterpretation by administrators remains H; the rendering mechanism is C.

**Keep:** unavailable is not empty, partial is not complete, conflicting identities stay separate, stale evidence retains original timestamps, retention expiry suppresses unusable evidence. **Correction:** S5/S6: current-kind and filtered count first, named actionable source exception summary, technical details behind one disclosure. No new “healthy” inference from missing data.

## F08 — Software wrapper adds a second navigation layer for the same destinations

**Affected view:** `/software`, especially Packages and Client versions.

**Observed flow:** click Software & licenses. See outer heading and links Packages/Client versions/Licenses/Winget packages/History, followed by Patch Management heading and Overview/Clients/Winget packages/History tabs. Both layers operate on the same section parameter. The old `/patchmanagement` route avoids the outer layer.

**Impact:** duplicate choices and different names for the same state; useful package rows pushed lower. This is directly observable and a true new wrapper regression, not a personal preference for a different visual style.

**Correction:** S7, render one workspace header and navigation. Keep the original specialist content and aliases. No need for a new layout framework.

## F09 — Group-member browsing has lost its table workflow

**Affected view:** canonical group profile, direct members.

**Cloud before/after:** the earlier member renderer used `CloudTable`: loaded search, A–Z/Z–A sort, 50-row pagination and inline typed name links. Current Entra direct members render an unpaged `<ul>` of member facts followed later by a separate list of member profile links. The normal GROUP_MEMBERS route maps to this canonical profile.

**AD-specific flow:** load a member page, filter it to one person. The facts list filters; the later `profile.relationships` list is not filtered. To inspect a matching user, the administrator must locate it again in that second list. General AD group support is new, so this portion is a new-feature weakness rather than a lost earlier general-group capability.

**Impact:** member recognition and navigation are separated; long groups require scrolling and repeated search. No live large-group timing claim. C09/C12 proves render/control loss.

**Correction:** S6: one bounded member table with facts and typed link in the same row, loaded-search/sort/page and explicit source-page controls. Keep unknown types unlinked and visible; no nested/effective-membership expansion.

## F10 — Section navigation no longer behaves consistently from the keyboard

**Affected views:** canonical Device and scoped User profile sections versus old Windows/AD detail tabs.

**Flow from code:** focus an old tab; ArrowRight/ArrowLeft changes the active tab using roving tabIndex. Enter a new profile; section controls are ordinary buttons in a nav with aria-current, without the old arrow behavior. Tab still reaches them.

**Impact:** different interaction contracts for similar-looking navigation within the same device journey, additional Tab stops, and no consistent tab semantics. This is not a claim that ordinary navigation links must always use ARIA tabs; the defect is the unreconciled same-task pattern.

**Evidence:** C10. Complete keyboard-only and screen-reader task execution remains unverified. **Correction:** S7: choose one semantic pattern, use it consistently, test actual focus and section behavior across the two shells and narrow wrapping.

## F11 — Full Event Log message is effectively a hover detail

**Affected view:** Windows Event logs.

**Flow:** a query returns a long message. The Message cell truncates it and supplies the full content only through `title`. No explicit expand/detail/copy action exists in that section.

**Impact:** difficult to inspect or copy a long event reliably with keyboard or a narrow window. The tool's purpose often requires the full message. **Origin:** unchanged before/current; not a consolidation regression. **Evidence:** C11. No live query executed here.

**Correction:** S8: expandable selectable message detail, accessible trigger and return focus; retain the same bounded query and transient-data behavior. Cancellation needs a separate implementation check before promising it in this section.

## F12 — Overview can continue to display previous capture state

**Affected view:** existing Windows Overview after Inventory/Security/Health activity.

**Flow from code:** Overview is kept mounted; it loads stored evidence for its host and on its own refresh. Completion callbacks invalidate Reporting for Inventory/Security, but do not consistently reload Overview; Health has no equivalent completion notification there. Returning to Overview can leave older displayed summaries until Refresh stored summaries.

**Impact:** an administrator can see an older assessment after collecting newer evidence in the same task. Displayed timestamps mitigate the problem; data is not silently relabelled as fresh. **Origin:** the same refresh boundary existed before consolidation. Earlier live acceptance used an explicit summary refresh, so it does not disprove this issue.

**Evidence:** C11 and baseline comparison. No live forced stale-cache reproduction. **Correction:** S8: refresh only stored summaries after successful local capture or show an actionable “new stored results available” control. Do not trigger a new scan.

## F13 — Product labels and tool placement encode the migration history

**Affected views:** Overview cards, sidebar, object pages, Data sources, Software and Windows profile.

**Examples:** Overview still links Clients/Active Directory/Report export while sidebar says Devices/Data sources/Reports. Canonical Health & security leads to distinct Health and Security tools. Software Packages corresponds to inner Overview. AD domain analysis and hygiene are filed under Data sources despite being independent administrative work; the same page is reachable via an old Overview shortcut.

**Impact:** learning a label does not reliably predict a destination. Sources legitimately belong in connection setup and evidence detail, but domain/DC analysis is not merely connection configuration.

**Origin:** current mixed naming is partly new wrapper debt and partly old terms retained for compatibility. Exact preference for “Devices” versus “Clients,” accent color, or link versus tab styling alone is P3, not a proven problem.

**Evidence:** O01/O08; C01/C08. **Correction:** S7 plus S4: a small terminology map and named task shortcuts. Restoring AD as its own primary navigation entry conflicts with ADR 0022 item 1 and requires a deliberate amendment; exposing an AD analysis shortcut under existing navigation does not require a new abstraction.

## F14 — Some existing specialist pages still lose filters on route unmount

**Affected views:** Action Center and retained AD user query; potentially further component-state workspaces requiring separate confirmation.

**Flow from source:** apply a query/filter/page, navigate to an object detail and return through the specialist root. Filter/page state initialized with `useState` is recreated on unmount. The endpoint view cache in Users is not a complete query/filter/page cache.

**Impact:** repetitive setup across list/detail cycles. **Origin:** pre-existing. Do not attribute all context loss to the new object model or claim every specialist route fails to restore identically.

**Evidence:** C14; no live Action Center filter cycle in this audit. **Correction:** extend S1 only after its focused canonical fix, using route/history state for concrete affected pages. Verify each actual origin separately; no global navigation-state framework.

## F15 — Stored-read failure is not the same as no saved result

**Affected views:** initial stored reads in Windows Inventory, Security and Health.

**Reproduction from code:** reject the cache-only Inventory request with a bridge/provider error; its catch returns idle for every error. Reject `getLatestScan` or `getLatestDiagnostics`; their catch handlers also return idle. The resulting pages say No inventory yet / No security scan yet or offer the initial Health run, instead of explaining that the previous result could not be read.

**Impact:** the administrator is steered toward a new scan when the problem may be reading an existing result. Unknown availability is presented as absence. Later explicit scan/refresh errors have better handling; this finding is specifically about initial reads.

**Evidence:** C17, unchanged before/current files. No live failure was injected into the company session. **Correction:** S8: distinguish known-not-found from read failure, with a stored-read retry that does not start a scan.

## F16 — Hidden Inventory content can perform a live BitLocker read

**Affected view:** `/clients/:host?target=exact`, including Overview or another selected tab, when a stored inventory snapshot exists.

**Code-proven flow:** the Windows shell mounts Inventory, Security, Health and other panels even when hidden. Inventory loads its cached snapshot; SnapshotGrid mounts `EncryptionCard`; the card's effect invokes `inventory/getDiskEncryptionStatus`. The handler delegates to `DiskEncryptionService`, which calls the live `IDiskEncryptionStatusReader`, without a cached-result path. A stored hardware snapshot therefore does not make this entire page opening stored-only.

**Impact:** an additional local/remote target read can run before the administrator chooses that check, and its loading/error result may be hidden in another tab. The retained in-flight-panel behavior is useful, but first-mount side effects must be controlled separately.

**Origin and limits:** pre-existing in the before version; not caused by consolidation and not an observed network trace here. This is a bounded read, not a write, auto-elevation or arbitrary command. It conflicts with ADR 0018 item 7's stored-only Client 360 opening and the related explicit-read boundary in ADR 0022. No ADR or implementation was changed by this audit.

**Correction:** S2: make the live encryption read explicit and show its own result time/status. Keep panels mounted for already explicitly started work if needed. Test the full Windows shell with a cached inventory fixture, not SnapshotGrid in isolation.

## Findings not established / design preferences

- “Devices, Users and Groups do not fit any administrator”: not established. They help object-oriented investigation; source-native accounts/groups and cross-links are real improvements. Task-first fleet analysis also needs a first-class place.
- “The new app lost Health, Event Logs, Cleanup, comparison, exports or package/print management”: not established. They remain routed; several are less visible or reached through a different shell.
- “Source scope, uncertainty and provenance should disappear”: rejected. Their importance is technical correctness, not a style choice. Their **placement and repetition** should change.
- “No pagination / no responsive shell”: false as a general claim. Shared lists have pagination and the shell has responsive controls. Specific Entra member controls are missing; the narrow native retest is incomplete.
- “Old directory initial failure means the old UX was worse”: not established. The before live read failed, but the cause was not investigated and is not a controlled speed comparison.
- “The application wastes space on every monitor” or “the colors violate contrast requirements”: H. Maximum content width and muted styles warrant checking, but no complete viewport/contrast measurement was conducted.
- “Every missing association is a bug”: false. Names, UPNs, local profiles and incomplete source sets cannot establish the relationships deliberately excluded by ADR 0022.
