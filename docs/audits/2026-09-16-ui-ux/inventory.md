# UI and function inventory

Audited application: `664bbe4604801a19b88724ac3beaf0ac48cc0ecc`. Baseline and observation meanings are in [evidence.md](evidence.md). This inventories the complete registered UI surface and its function families from source. Live coverage is a subset, marked O below; C means source inspection. A registered route, retained component or passing test does not establish convenient discoverability.

The [CSV annex](source-ui-manifest.csv) lists 985 source declaration sites in 90 files, extracted from all 108 production TSX files under `frontend/src`, excluding tests. It includes buttons, inputs, selectors/options, labels, headings/cards, links, tables, disclosures, forms and role-bearing elements. Dynamic labels and destinations are marked `{dynamic}`. A mapped control produces one declaration site but may render many controls. Shared components and compatibility renderers are included, so this annex deliberately does not claim every declaration is independently reachable at runtime. Runtime-conditioned reachability is described below.

## Complete route registry

Source: `app/routeRegistry.tsx`, 32 route definitions. **13** have sidebar navigation; **19** do not. Before had **18** routes and **14** sidebar destinations. Extra routes mainly support scoped identities, resolvers and retained specialist work; route growth is not itself a UX defect.

| # | Route | Entry / visible parent | Actual surface and status |
| --- | --- | --- | --- |
| 1 | `/` | Work → Overview | Existing Dashboard cards, stored-source coverage and operational shortcuts; title changed. O/C |
| 2 | `/actions` | Work → Action Center | Computed findings/work list, source progress, details and task links. C |
| 3 | `/devices` | Objects → Devices | Loaded object working set; primary replacement for Clients. O/C |
| 4 | `/users` | Objects → Users | Loaded account working set; primary replacement for AD user query. O/C |
| 5 | `/groups` | Objects → Groups | Loaded source-native group working set; new top-level capability. C |
| 6 | `/software` | Operations → Software & licenses | Package/versions/license/Winget/history wrapper. O/C |
| 7 | `/vulnerabilities` | Operations → Vulnerabilities | Existing Nessus Overview/Assets/Findings/Scans workspace. C |
| 8 | `/printmanagement` | Operations → Print Management | Existing physical printers, server/queue management, checks, history and export. C |
| 9 | `/networkscan` | Operations → Network Scan | Existing explicit subnet discovery and results. C |
| 10 | `/reporting` | Operations → Reports | Existing single-subject stored Inventory/Security HTML/JSON export. C |
| 11 | `/sources` | Administration → Data sources | Management connections, Active Directory and Microsoft 365 sections. O/C |
| 12 | `/settings` | Administration → Settings | Saved configuration, validation, policy, secure connection settings. C |
| 13 | `/logs` | Administration → Error log | Existing error list, level filter, details and hide-previous marker. C |
| 14 | `/cleanup` | Devices link; global search; finding links | Existing read-only assessment; formerly direct sidebar entry. C |
| 15 | `/clients` | Devices → Client posture, saved targets and batch scans; existing shortcuts | Existing operational Clients page. Secondary route, not deleted. Before surface inspected live. C/O-before |
| 16 | `/clients/compare` | Devices → Compare; Clients → Compare; global search | Existing two-host stored comparison. C |
| 17 | `/clients/:host` | Old deep links, saved targets, findings, reports | Compatibility resolver normally opens scoped WEC profile. `target=exact` retains Windows detail shell. `section` aliases preserved. C |
| 18 | `/devices/:source/:scope/:objectId` | Working-set native-ID link; relationships; old host adapter | Canonical device profile with seven areas. C |
| 19 | `/devices/records/:source/:workspace/:snapshot/:index` | Working-set management source-record link | Exact KSC/opsi/Nessus/AD snapshot record; scope/expiry and separate candidates. C |
| 20 | `/users/workspace` | Compatibility/development-era alias | Same Users working-set component. No separate function. C |
| 21 | `/groups/workspace` | Compatibility/development-era alias | Same Groups working-set component. No separate function. C |
| 22 | `/users/directory` | Users → AD query workspace | Retained AD query/filter/sort/page view. C; before equivalent O |
| 23 | `/users/:objectId` | Legacy AD user links | AD detail adapter reads verified scope and routes to canonical account, preserving section/endpoint. C |
| 24 | `/users/resolve` | Stored user SID links | Explicit SID → AD account resolution, typed errors and verified profile link. C |
| 25 | `/users/:source/:scope/:objectId` | Native account links and relationships | Canonical account profile, six sections. O/C |
| 26 | `/groups/directory` | Groups → AD group query workspace | General bounded AD group query; new specialist view. C |
| 27 | `/groups/resolve` | AD user direct-group DN links | Exact-DN group resolution, explicit action then native profile. C |
| 28 | `/groups/:source/:scope/:objectId` | Group/member/relationship links | Canonical group profile, vertically stacked source and member cards. C |
| 29 | `/microsoft365` | Compatibility links; working-set connection link | Original source workspace; resource navigation now maps to canonical lists/profiles/license workspace. Connection/tenant remain source-owned. C/O |
| 30 | `/activedirectory` | Legacy links, Overview card; Data sources → AD | Original AD domain, DC, hygiene and privileged-group analysis. C |
| 31 | `/employeelifecycle` | Old bookmarks only | Allowlisted redirect to Clients/posture. Frozen legacy lifecycle data is not an active CRUD UI. C |
| 32 | `/patchmanagement` | Old bookmarks/Overview shortcut | Original Patch workspace without the new outer wrapper. Same operational functionality; currently the shorter visual route. C |

Old aliases are not automatically redirects to the same visual shell: `/patchmanagement` and `/activedirectory` still render their specialist pages, while `/clients/:host` ordinarily resolves into another shell. This distinction matters when verifying “deep link preserved.”

## Shell, navigation and shared interactions

| Surface | Inventory | Reachability / behavior |
| --- | --- | --- |
| Desktop shell | Grouped Work/Objects/Operations/Administration rail; active section; version/runtime-profile footer; Open log folder. Top bar section label, search, admin sign-in/status, explicit elevation restart. | Every route. Sidebar and body have separate scrolling. Outer content has a maximum width; no visible Back control. O/C |
| Narrow shell | Navigation toggle, overlay drawer, close button, Escape close/focus restoration; wrapping top bar and profile controls. | Responsive CSS at rail breakpoint. Source/tests inspected; independent narrow live run incomplete. C |
| Global search | Ctrl+K, close/Escape, combobox, result categories Navigation/Users/Devices/Groups/Saved targets, arrows/Home/End/Enter, focus trap; up to six loaded results per object kind. | Local displayed working-set revision plus navigation and non-Client saved targets. No explicit source-query continuation. C |
| Admin sign-in | Account/password entry, sign in/out and session identity feedback. | Session credential surface; inventoried from code, no credential values read or entered. Distinct from cloud authentication and elevation. C |
| Saved targets | Role-aware target list/pickers, add/remove, target label/host/role/account metadata; credentials remain separate. | Used by operational Clients, directory setup, print servers, comparison and reporting. Generic saved entries do not constitute confirmed physical-device identities. C |
| Tables | DataTable sorting where supplied, row action/keyboard handling, loading/empty messages, overflow container, pagination/size selector where supplied. Printer table also has its own expansion/sorting. | 31 DataTable declaration sites and three native table sites, plus dynamic reuse; not every table opts into paging or sorting. C |
| Error/loading primitives | Spinner/status; structured ErrorState with cause, next action, optional technical details and retry; compact errors; raw role-alert strings in many new profiles. | Uneven composition: old structured views explain recovery better than several new raw error paragraphs. O/C |
| Dialog/overlay families | Global search; mobile navigation; admin sign-in; package preview/confirmation panels; print port deletion confirmation; native file save/open and external launch prompts; WAM login and UAC. | Conditional controls, not additional routes. Package/print/native writes were not invoked. Exact controls appear in the CSV; no generalized dialog framework is proposed. C |

## Object list workspaces

All three share a coverage panel, cached-source check, pending-update apply action, explicit source loader, current-set search, identity filter, name sort, and 25/50/100-row pagination. Filtering/sorting precede displayed pagination. Links preserve a source/scope/native reference. This improves identity reliability but produces a technical default presentation.

| Area | Fields, actions and links | Missing / secondary / duplicate |
| --- | --- | --- |
| Coverage | Loaded source records; displayed objects/addresses; scoped-identity count; unresolved/candidate count; limits/omissions; collapsed per-query availability, freshness, timestamps, cached/indexed counts and error. Check cached sources; Apply available updates. | Counts cover all kinds in the shared snapshot, not just the current page/kind. Source failures/age mainly inside disclosure. C07 |
| AD loader | Directory DNS scope, optional server, source query; page number for users/groups; explicit bounded read; cancel/error. Computer query returns up to 100. | Separate from the loaded text filter. Empty default DNS scope blocks this new action; old directory workspaces can use machine-domain/saved-DC setup. O/C |
| Management/stored loader | Devices: stored-address query and Load matching stored addresses; Read configured management sources; dedicated cancel; source error. | Most source detail retained outside primary table. Generic and management Cancel overlap (F05). O/C |
| Cloud loader | Read bounded Entra inventory for selected object kind; Devices additionally Intune; Microsoft 365 connection/source-analysis link; cancellation and errors. | Connection alone does not fill a working set. No tenant search on every keystroke. O/C |
| Devices filters | Search loaded devices; source; exact OS; separate AD account and Entra account states; Intune compliance and management; saved/scanned evidence; identity assessment. | No posture, severity/finding, grouping or batch selection in this primary view; links to Clients, Compare, Cleanup above. C/O |
| Users filters | Search loaded users; source; enabled/disabled/unknown account state; identity assessment; raw assigned SKU ID. | Department/OU query and richer AD sorting only in AD query workspace. SKU query can be prefilled by license navigation. O/C |
| Groups filters | Loaded search, source, identity assessment; name sort/page. | No list member counts or privileged context; explicitly says they are not evaluated. General AD group query is a separate link. C |
| Row fields | Object name, individual native-ID or source-record links; source evidence with scope, OS/account/Intune state, SKU count/department or group type/security/mail; stored evidence/time when supplied; identity assessment/candidate warnings. | Name itself is not clickable; repeated labels/GUIDs and explanations dominate. UPN, device trust/last-sign-in and Intune user/last-sync not retained as first-level list columns. O/C |

### Device profile and Windows detail

| Surface / section | Visible content and actions | Cross-links / limits |
| --- | --- | --- |
| Canonical profile header | Title, source/scope, Devices root link, Reload cached evidence, Cancel read, source errors. Identity explanation and raw reference card appear across sections. | Opening profile reads stored/cached evidence; exact Windows host is required for Windows execution. C |
| Overview | Identity; exact-target Windows tool links when eligible; relationship/candidate counts; source freshness/coverage metadata. | Full existing OS/CPU/RAM/disks/software/Health overview is one further Windows overview link away. No compact operational synthesis here. C |
| Identity & management | AD computer fields and explicit GUID read; separate Entra inventory/object, Intune inventory/object reads; KSC/opsi/Nessus candidate disclosures; source times/counts/errors; explicit management load/cancel. | Native IDs and source differences preserved. Nessus source findings link; Microsoft 365 source setup link. C |
| Inventory | Exact WEC target transitions to old Windows Inventory section. Cloud-only target gets Windows target required explanation. | Does not discover or choose an execution host from a name match. C |
| Health & security | Stored Health/security evidence; separate Open Windows Health and Open Security history/checks; per-enrollment Intune compliance, management and sync evidence. | Candidate cloud compliance is not assigned to a Windows host. Overview also offers direct Health link, so an extra Health click is not unavoidable. C |
| Relationships | Stored observed users, SID resolution; Entra registered owners explicit read; source-ID relationships; separate candidates; stored Windows candidate-query coverage. | Typed account/device/group links. Owner/associated user is not inferred primary user. C |
| Event logs / Printers | Exact-target transition to Windows sections, otherwise target-required explanation. | Existing query tools preserved; shell/tabs change during transition. C |
| Management record profile | Source/scope/snapshot/record locator, raw source fields, observation metadata, source errors, candidate matching explanation, bounded candidate links. | Exact Nessus findings link; opsi workspace link; originating working-set URL; expired snapshot prompts reload rather than guessing another record. C |
| Windows header | Exact full target, local/remote identity; All clients; return-to-object link when state supplied; save/unsave target; PowerShell action/error. | Remains under `target=exact`. No PowerShell session opened in audit. C |
| Windows Overview | Device/OS hardware summary, Health, installed software, security posture, linked users, management-source assessment; explicit stored-summary refresh. | Existing useful operational summary. Source unavailability and stale facts distinguished. C |
| Windows Inventory | Stored hardware snapshot: OS/CPU; memory-bank manufacturer/part/capacity/speed; disk model/size/interface; active network IP/MAC/link speed and disconnected-adapter disclosure; GPU/driver and monitor identity; installed-software name/version/publisher disclosure; explicit capture/refresh and previous snapshot on refresh failure. BitLocker volume/protection/elevation/error card. | Software table has nested scrolling but no search/sort/page controls. BitLocker performs a separate live read on mount, including when this panel is hidden in the Windows shell (F16). C |
| Windows Security | Explicit scan; check/severity/status details and errors/elevation requirements; scan history selection; prior persisted results. | Historical records preserved. C |
| Windows Health | Explicit run/re-run; update age, selected services, Event Log summary and free disk; latest saved result. | No history selector in this UI. The diagnostic store is latest-per-host with replace-on-save; preservation of legacy data through the earlier reduction is not a browsable run history. Narrow ADR 0018 scope, not complete security assessment. C |
| Windows Event logs | Seven presets: System errors 24h, System warnings 24h, Application errors 24h, Application crashes 7d, unexpected shutdowns 30d, service failures 7d, disk errors 7d. Run query; result count/table time/level/source/event/message. | Full message only title/hover; no separate message detail or cancel action. Pre-existing limitations. C |
| Windows Printers | Explicit client CIM printer read; name/driver/port/location/local-network/default information; result/errors. | Separate from server/SNMP fleet tools. C |
| Windows Report export | Stored source readiness, included-data summary, HTML/JSON selection, export/open result/errors. | Existing export boundary, no cloud export added. C |
| Windows Microsoft 365 | Existing source context panel with scoped/candidate cloud evidence and source links. | Retained compatibility section alongside canonical Identity & management. C |

The Windows shell has **eight** tabs (Overview, Inventory, Security, Health, Event logs, Printers, Report export, Microsoft 365) and roving arrow-key navigation. The canonical device shell has **seven** regular section buttons. Shared subject identity alone does not make these two shells one coherent profile.

### Accounts and groups

| Surface / section | Functions and data | Reachability / scope |
| --- | --- | --- |
| AD user query | Directory connection/saved DC/current identity, refresh; name/account/UPN/employee-ID, department, OU, enabled filters and Apply; sortable name/account/department/last-logon columns, paging; account state and profile entry. | Secondary `/users/directory`, formerly primary Users. Creation time is a supported sort enum but not a separate visible column; do not confuse contract capability with a visible control. C/O-before |
| Account Identity | Source reference(s), AD scope input/use/exact-SID resolution; AD identity/status/organization/contact/timestamps when present; Entra identity/account fields; explicit bounded-user/object/exact-SID queries and separate candidates. | Entra-only account supported. AD and Entra states remain separate. Technical reference and AD loader precede Entra facts in observed cloud profile. O/C |
| Account Groups | AD direct group list/DN resolver; explicit Entra direct-group read and typed group links; independent coverage/errors. | Direct membership only; no effective authorization. C |
| Account Devices | Stored Windows user evidence; associated/registered cloud devices and exact native links; explicit source queries and evidence limitations. | No inferred ownership from display names or local profiles. C |
| Account Licenses | Explicit per-user assignments/service plans; tenant catalogue and SKU links; null/unavailable assignments distinct from zero; disabled-plan information. | SKU → tenant catalogue → loaded assigned accounts. C |
| Account Activity | Explicit optional sign-in and registration report reads; timestamps, MFA registration/capability/method categories, permission/lag explanations. | AD timestamps remain under Identity. Registration is not enforcement; not full mailbox/access audit. C |
| Account Leaver review | Existing AD-focused evidence, last-logon/activity hints, direct access, device-return evidence and read-only Markdown review export. | No lifecycle status workflow or directory writes. Entra-only account is not silently treated as AD leaver. C |
| General AD group query | Directory DNS scope with existing endpoint context, bounded group query, previous/next source page, source metadata, linked group name and DN. Separate bounded Entra group inventory button and scrollable name links. | New capability beyond four privileged groups; no user-selectable sort in this query page. GUID/SID/type/scope facts are in the selected profile. C |
| Group profile | Header/root return, cached reload/cancel; identity; AD group GUID read and facts; AD direct-member page read, previous/next and local loaded-page filter; cloud identity reads; cloud direct-member read. | Vertically stacked, no section tabs. C |
| Group member display | AD member name/class/GUID/SID/DN and limited-info notice; Entra member name/type/ID/UPN; separate Direct member profiles list with typed links. | AD filter does not filter separate links. Entra member list has no equivalent to old CloudTable search/sort/page. Unknown member types remain visible but unlinked. C |
| SID / DN resolvers | Explicit exact lookup, loading/error/ambiguity states, valid native profile navigation. | Additional deliberate identity-resolution step where only a SID/DN observation exists. Preserve safety; do not replace with first-name-match selection. C |

### Microsoft 365 source presentations and canonical destinations

The original generic data renderer remains in source and is reused for appropriate evidence and license presentation. Its existence is not proof that the old list is normally reachable: `cloudObjectDestination` redirects the resource selection. Fields missing from a default list generally remain in source/profile detail.

| Resource family | Canonical destination | Previous first-level information / current placement |
| --- | --- | --- |
| Tenant/authentication | Data sources → Microsoft 365 | Tenant, enabled features, configured permissions, signed-in/connected state, Sign in/Disconnect, source-query controls/status. |
| Users / User / exact SID | Users Entra filter / scoped account | Old Users table linked display name and showed UPN, enabled, department, license count. New list substitutes evidence/scoped links; full fields in Identity. |
| Groups / Group / UserGroups | Groups Entra filter / scoped group / account Groups | Old group table rendered readable M365/security/distribution and assigned/dynamic type. Current list primarily exposes source group-type values and warnings. |
| Devices / Device / UserDevices | Devices Entra filter / device / account Devices | Old list OS version, trust and last sign-in; current detail has fields, default list does not show all. |
| ManagedDevices / ManagedDevice | Devices Intune filter / enrollment profile | Old list associated-user link, OS version, compliance and last sync; now individual enrollment source fields/relationships. All enrollments retained. |
| GroupMembers / DeviceOwners | Group members / device Relationships | Old direct-member CloudTable searched, sorted and paged loaded members and linked names inline. New group profile separates facts and links. |
| Licenses / UserLicenses | Software Licenses / account Licenses | SKU part number, native ID, enabled/consumed/remaining seats, status, service-plan disclosure, assignment/capacity warnings; reverse loaded-assignment link. |
| UserActivity / UserRegistration | Account Activity | Optional report reads remain distinct; no new Graph permission or hidden collection implied. |

## Independent workspaces and existing tools

| Workspace | Inventory of actions, controls and detail views | Retention and connections |
| --- | --- | --- |
| Overview | Source-status/coverage/time cards for Nessus, stored clients/inventory, security, printing, opsi; AD/report shortcuts, patch status. | Existing dashboard, not a universal object summary. Mixed old/new vocabulary. O/C |
| Action Center | Computed finding summary; source load/progress/cancel/retry; severity/source/query controls with Apply, sorting and pagination; list/context presentation; individual finding/detail/task links. | Links to relevant host section, exact Nessus asset, Cleanup evidence and specialist page. No owner/notes/workflow/remediation. C |
| Operational Clients | Environment source progress and status, refresh/cancel/retry; search device/OS/finding; posture/source/group filters, sort/page; selection and bounded batch workbench; explicit page connectivity; saved clients; Compare. | Batch operation select/run/cancel/progress and per-host result/history. Direct link from Devices, still compatible with `posture`. No batch executed. C/O-before |
| Compare | Two searchable saved/stored/local target pickers, recent targets, reload selection sources, compare action, coverage/errors per side; hardware, installed-software and security differences. | Stored data only; All clients return. Missing data differs from retrieval failure. C |
| Device Cleanup | Load read-only assessment, source status, classification/search/review filters, paging; device/reason/AD/latest-source/review table; selected evidence details; explicit connectivity for eligible targets; per-session decision and reason; Markdown and filtered Excel export. | No AD delete, automatic probe, persistent decision workflow or name-based targeting. Ambiguous rows require individual evidence. C |
| Active Directory analysis | Connection/domain/server fields and saved DC, test/analyze actions, domain counts/DC table; hygiene rule summary → filtered/paged rule detail and CSV; four privileged-group member browsers. | `/activedirectory` and Data sources AD section. General group discovery is separately under Groups. C |
| Nessus Overview | Refresh Nessus, sync status/progress/errors/retry, severity summary and 7/30/90-day trend selection/coverage. | Stored included completed scans; no implication of complete fleet. C |
| Nessus Assets | Search host/plugin/CVE, severity, sortable/paged asset table with source key and severity counts, selected asset context. | Exact source-record links retained; legacy address filter may retain multiple candidates. C |
| Nessus Findings | Sortable/paged finding list; plugin/CVE/severity/count; selected detail/instance panel, close/retry, instance evidence and source scan links. | Panel is in-page, not a new route. C |
| Nessus Scans | Scan list/status/time/coverage; included scan context. | No vulnerability remediation/write. C |
| Software Packages | Depot/location selector, connection state, refresh, package/managed/update/client/failure summary; product search; product/management/depot version/latest Winget/depot deviation/client-behind/status table; selection and package detail/update controls. | Outer wrapper duplicates inner Patch navigation. Original `/patchmanagement` keeps one layer. O/C |
| Software Client versions | Search client/depot/version, search package, workflow-state filter, sort/page, connection/retry; client/package/depot/installed/target/status columns. | Read-only deployment state and exact eligible client links. Client rollout remains in opsi. C |
| Winget packages | Catalog search and results, package/version/installer eligibility, depot selection and product-ID mapping, explicit adoption/create/update previews and confirmation, selected batch results and failure details. | Existing gated SCP/build/install path; no automatic rollout. Ineligible installers explained. Writes inventoried only. C |
| Package History | Refresh/filter and audit table/detail for checks/builds/adoptions/results and failure context. | Historical removed workflow records preserved; removed pre-ADR0017 actions are not restored. C |
| Print servers | Saved server selection/registration/removal, selected/all scan controls, per-server load/error states and stored snapshots. | Saved targets and existing Windows credential context. C |
| Printer inventory | Search; site grouping/filtering and sort; compact physical-device rows; expand queues; model/serial/location/status/toner/notify/IP/web-UI/network; source discrepancy and stale data hints. | Device web UI explicit external launch; no hidden canonical device merge. C |
| Print checks | Explicit selected-fleet/per-device notification configuration read; session password input; DHCP server input/check, network policy and reservation status. | Read-only SNMP/CIM/DHCP/CCRX behavior, independent error/unavailable states. Not executed. C |
| Print history/export | Lease-swap snapshot selection/diff/new/gone/swapped; filtered CSV column selector, ordering and export feedback. | Serial-based history retained; export is bounded to selected view. C |
| Unused printer ports | Server selection, inspect unused ports, select, delete preview/confirmation, result/error. | Existing write gate retained, never invoked here. C |
| Network Scan | Target/subnet input, optional DHCP server, scan/cancel/retry/status; IP/hostname/type/status/open ports/MAC vendor results and reservation context. | No current canonical device/profile continuation in result rows; pre-existing disconnect. No scan executed. C |
| Reports | Single target selection; stored Inventory/Security readiness/time/coverage; source links; include-data summary; HTML/JSON export, save/open feedback. | No parallel cloud report or automatic scan. C |
| Data sources | Management/Active Directory/Microsoft 365 navigation; KSC session override, opsi session connect/disconnect, Nessus configured-credential status and specialist links; source availability. | Session management coexists with saved Settings. AD and M365 embed existing full pages, including their headers. O/C |
| Settings | Overview/effective configuration; Environment Health; Vulnerability Management; Patch Management; Microsoft 365; policy. Connection tests, configuration editing, secure credential add/remove status, save/reset, validation, dirty-section and restart-required indication. | Old section anchors preserved. No setting saved; no credential value inspected. C |
| Error log | Refresh; severity/level filter; time/level/source/summary table; selected full error/technical detail; Hide previous entries; open log folder. | Hiding advances a display marker, not deletion of logs. Live logs not inspected. C |

## Missing, less accessible and duplicated functions

| Capability | Classification after consolidation | Recommended treatment |
| --- | --- | --- |
| Find a previously unloaded AD account/computer from global search | Removed from this entry point; provider capability remains in explicit source/query views. | Restore **explicit** bounded continuation for the entered query, not per-keystroke provider calls. |
| Device operating-state overview | Still present in secondary Windows shell; absent from canonical Overview. | Render the existing summary at the canonical overview with source-specific attribution. |
| Posture/finding filters, saved targets, batch scans | Still present behind Devices → Clients link. | First-level task mode within Devices, preserving exact targets and scope. |
| AD department/OU filters and useful sort columns | Secondary AD query workspace. | Make this a named visible account-query mode; do not force all sources into one filter schema. |
| UPN and human-readable name entry in cloud user table | Replaced by technical native-ID link; UPN only in detail. | Restore UPN column and clickable name while retaining exact address in the link. |
| Cloud device trust/last sign-in and Intune user/last-sync at list level | Detail-only after canonical replacement. | Restore task-relevant source-specific columns or compact source modes. |
| Cloud group-member search/sort/page and inline links | Not available in the normal new group profile. Existing generic renderer remains in source. | Reuse bounded member table behavior with typed inline navigation. |
| Health history browsing; local inventory software search | No Health history selector and no software-table search in either compared version. | Record as existing limitations. A history feature requires a separate retention/product decision; software lookup can use a small local filter. |
| Working-set return context | URL/history support exists; real profile root link bypasses it. | Visible Back to results with safe deep-link fallback. |
| Source progress/cancel semantics | Old detailed progress in Clients; new source control has reduced feedback and miswired generic cancel. | One operation status and one correctly bound Cancel. |
| Old Microsoft 365 connection route | Legacy route remains used by new links. | Standardize route/label without removing direct source access. |
| Software tabs/headings | Duplicated in wrapper and embedded page. | One navigation row; direct old route remains compatible. |
| All-object links on each object list | Repeat sidebar destinations. | Keep only useful cross-task navigation; evaluate after dense list layout, not as a P1 defect alone. |
| Windows versus canonical device sections | Two different profiles for the same exact execution subject. | Consolidate the visible journey using existing sections; retain safety checks at each operation. |
| Employee Lifecycle CRUD, package pilot/rollout/manufacturer mapping | Already frozen/removed by earlier decisions. | Not consolidation regressions and not proposed for restoration. |

No other complete specialist tool deletion was established. G2 route preservation is largely supported by the code; G2 convenience and end-to-end parity are not demonstrated merely by the retained routes.
