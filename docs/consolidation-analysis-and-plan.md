# WEC consolidation analysis and implementation proposal

Status: **Proposal only — no implementation authorized by this document.**

Analysis date: 2026-09-14. Inspected branch: `codex/microsoft-365-read-only`.
Inspected application commit: `8e3640d`. The worktree was clean at the start.

This report responds to the explicit request to analyse the current application
and plan consolidation without changing application code. The only deliverable
added by this analysis is this report. It does not amend an accepted ADR, start
a roadmap slice, authorize new collection, change authentication or initiate
provider queries. Document language follows the repository's English convention.

Recommendation: consolidate **Devices, Users and Groups through composed read
models**, preserving each source's identity, status and freshness. Fix identity
loss in hostname-based merging before expanding the joins. Deliver useful
device and account profiles first; change the main navigation only after
feature parity. Current data cannot establish a confirmed four-source physical
device identity in every case, so candidates must remain explicitly separate.

Report navigation:

- [A. Current state](#a-current-state)
- [B. Prioritized problems](#b-prioritized-fragmentation-and-trust-problems)
- [C. Proposed information architecture](#c-proposed-information-architecture)
- [D. Technical and logical model](#d-proposed-technical-and-logical-model)
- [E. Identity matching](#e-identity-matching-strategy)
- [F. Source of truth and conflicts](#f-source-of-truth-and-conflict-policy)
- [G. Migration phases and feature preservation](#g-migration-plan-for-a-later-explicitly-started-implementation)
- [H. Before / after](#h-before--after-at-a-glance)
- [I. Open decisions](#i-open-decisions-before-implementation)
- [Evidence index](#evidence-index)

## Basis, confidence and boundaries

The audit covers the registered application routes and modules, their relevant
production components, bridge contracts, data owners, matching rules, persistence
and cache boundaries, plus existing test coverage and UX decisions. It is a
source-code and architecture analysis, not a new desktop usability test or a
tenant data-quality survey. Examples below are synthetic.

The user reports that the Microsoft 365 integration works. This is accepted as
user-supplied operational context; it is not a claim that this analysis reran
tenant acceptance. No company credentials, databases, logs or tenant inventories
were read for this report.

Authoritative decisions remain ADRs 0001–0007, 0010, 0013 and 0017–0021 and
`ROADMAP_DECISIONS.md`. In particular:

- Keep the .NET 10 WinForms/WebView2 modular monolith and typed bridge.
- Keep read composition through concrete Core contracts; modules reference
  neither Infrastructure nor one another.
- Keep Graph/AD writes absent, cloud data session-only, and scans explicit.
- Preserve the existing narrowly authorized Patch/Print actions and their gates.
  WEC as a whole is not exclusively read-only.
- AD remains authoritative for an AD account. Adding cloud-only accounts to
  User Management requires a deliberate extension of ADR 0019, not a silent
  change of its identity policy.
- This plan must not revive frozen Employee Lifecycle CRUD or delete its tables.

The older UX audit is historical evidence. Its roadmap records all 22 findings
as completed. Current code confirms substantial fixes: shared paged tables,
semantic status badges, cancellation/progress, responsive navigation, lazy
routes, searchable comparison and report invalidation. Those original findings
are not repeated here as if still universally open. New gaps are identified
against the present code.

The execution register still describes an early M365 slice, whereas the code and
M365 implementation report contain the completed integration. The earlier report
records a file-lock problem. This analysis leaves that register untouched and
uses the commit and current code as its implementation baseline.

## A. Current state

### A1. Actual navigation

There are **14 sidebar destinations, 18 registered routes and 15 registered
backend modules**. Clients, AD computers, Entra devices and Intune devices are
not four independent top-level areas in the present application.

| Sidebar destination | Route | Actual purpose and subviews |
| --- | --- | --- |
| Dashboard | `/` | Stored/module summaries for clients, local Security, printers, opsi, Nessus and AD navigation; no integrated cloud overview |
| Action Center | `/actions` | Computed findings from hygiene, stored Inventory and Security; source/severity/search/sort/page; optional context map |
| Device Cleanup | `/cleanup` | Candidate list and selected-host assessment, session decisions, explicit connectivity, Markdown and filtered Excel exports |
| Clients | `/clients` | Fleet posture and merged client list, source/status filters, OS/site grouping, comparison, explicit batch scans |
| Users | `/users` | AD-backed paged account inventory and User 360 |
| Microsoft 365 | `/microsoft365` | Connection; Overview, Users, Licenses, Groups, Entra devices, Intune devices; selected cloud object/relationship resources |
| Active Directory | `/activedirectory` | Domain/DC overview, object counts, hygiene rules and four SID-resolved privileged groups; embedded identity/member browsers |
| Vulnerabilities | `/vulnerabilities` | Nessus Overview, Assets, Findings, Scans; trend and finding detail panel |
| Patch Management | `/patchmanagement` | opsi Overview, Clients, Winget packages, History; controlled depot package maintenance |
| Print Management | `/printmanagement` | Physical printer list, server/queue evidence, supplies, DHCP/notification checks, lease history, export and unused-port workflow |
| Network Scan | `/networkscan` | Explicit nmap discovery with optional DHCP reconciliation |
| Report export | `/reporting` | Selected-machine Inventory/Security report; not a fleet aggregate report |
| Settings | `/settings` | Effective configuration, Environment Health, Vulnerability Management, Patch Management, Microsoft 365, policy |
| Error log | `/logs` | Filtered application errors, details and view-history marker |

The four non-sidebar routes are `/clients/compare`, `/clients/:host`,
`/users/:objectId` and `/employeelifecycle`. The last is an allowlisted
compatibility redirect to Clients, not an active employee database UI. [S01, S02]

### A2. Entities, sources and overlap

| Current surface | Entity actually represented | Data sources / owner | Available information | Overlap and present limitation |
| --- | --- | --- | --- | --- |
| Clients list | Host-addressed operational device row | EmployeeLifecycle composition of AD, KSC, opsi, Nessus enrichment, Inventory host list and saved Client targets | Name, AD OS/description, source presence, stale/risk findings, Inventory time, saved flag | No Graph rows; hostname merge; does not enumerate all source-only records |
| Client Overview | Stored Windows subject plus optional management context | Inventory, Security, Diagnostics; separately loaded environment context | Hardware/OS/software summaries, coverage, Health/Security findings, observed users, AD/KSC/opsi/Nessus facts | Management and stored snapshots have separate lifecycles; M365 is another tab |
| Client Inventory | Windows scan target and its latest snapshot | CIM/WMI, local/remote registry, BitLocker seam | CPU/RAM/disks/OS/NICs/GPUs/monitors/software, optional user observations; explicit encryption read | Monitor serials exist; a computer/chassis serial, system manufacturer/model and Entra registration ID do not |
| Client Health / Security | Check result for a Windows execution target | Diagnostics and Security modules | Four Health checks; separate detailed security checks, coverage and Security history/diff | A finding, execution failure and missing evidence are different concepts |
| Client printers / Print Management | Installed printer connection vs queue vs physical printer | CIM, SNMP, DHCP, CCRX; PrintManagement | Local/client connections; server queues, physical serial/model/status/supplies, lease differences | These are related but distinct entities; should not be merged into “computer” |
| AD analysis | Directory, DC, hygiene subject and privileged group | LDAP; ActiveDirectory | Counts, DC identities, bounded examples/full rule pages, direct privileged-group member pages | No general AD group inventory or AD computer profile by immutable ID |
| Users / User 360 | AD account | DirectoryUserReadService + UserManagement | GUID/SID/account/UPN/contact/org/lifecycle/direct groups, linked-device summaries, Leaver | Cloud-only accounts are outside this workspace; groups mostly non-navigable text |
| M365 Users | Entra account | Graph reader + M365 session cache | Entra IDs, UPN/mail/enabled/type/org fields, SID/immutable ID, assigned SKUs; separate groups/devices/license/activity reads | Same hybrid account can be displayed here and in Users without a shared profile route |
| M365 Groups | Entra directory group, including Unified/security/distribution types | Graph | ID/name/type/mail/security/dynamic rule/visibility; direct members | AD equivalents cannot currently be matched reliably; no general cross-source group profile |
| M365 Entra devices | Device registration object | Graph | Object ID, registration device ID, name/OS/version/trust/enabled/approximate sign-in, owners on demand | A registration is not automatically one persistent physical asset |
| M365 Intune devices | Management/enrollment record | Graph Intune | Managed-device ID, Entra device ID, associated user, OS/version/compliance/management/enrollment type/sync/serial/model/manufacturer | Details are an inline disclosure; multiple enrollments remain ambiguous |
| M365 Licenses | Tenant SKU and account assignment/service-plan detail | Graph | SKU identifiers, enabled/consumed/remaining seats, capacity warnings; per-user plans/status | SKU capacity and a user's entitlement are different projections; no reverse assignee view yet |
| opsi / Winget | Client package state, depot product and managed package link | PatchManagement / opsi API / Winget | Installed/target versions, action state, product/depot versions, package catalog, build/check audit | Package installation does not establish an M365 license entitlement |
| Nessus | Asset, plugin finding, instance, scan and daily trend | VulnerabilityManagement / Nessus import | Host/FQDN/IP/UUID when present, scan times, findings/severity/ports/CVEs and scan provenance | Local asset key often uses a short hostname; name-only matching is not verified identity |
| Network Scan | Network observation / reservation | nmap and DHCP | Address, hostname, MAC/vendor, ports, inferred kind, reservation state | No durable WEC computer identity or canonical profile navigation |
| Saved targets | Address + operational role | Targets / SQLite | Client, PrintServer, OpsiServer, DomainController, Generic; label/user name | A bookmark and execution address are not proof of a physical device |
| Action Center / Cleanup | Computed assessment about another entity | Concrete evidence providers | Finding, source/age/coverage, recommendation; selected review state | These should lead to object profiles, not become new master entities |
| Frozen Employee Lifecycle | Historical employee/department/case/task/audit records | Five retained SQLite tables | Legacy records/source code preserved, CRUD handlers unregistered | Must not become a second identity or people database |

Sources: [S02–S18].

A noteworthy coverage asymmetry: `ItHygieneService` unions AD, KSC and opsi
keys, then enriches matching rows with Nessus. It does not union Nessus-only
keys. `ClientWorkspacePaging` additionally includes Inventory and saved
Client targets, but not Security-only hosts. Global search and the comparison
helper do include stored Security hosts. Thus “all clients” currently describes
a specific merged working set, not all known records throughout WEC. [S03, S04, S19]

### A3. Computer field availability

| Requested subject | Already present | Not currently provided / caveat |
| --- | --- | --- |
| Identity | Execution host; AD name/FQDN/DN/OU; source-specific opsi client ID and Nessus asset key; Entra object ID/device ID; Intune ID/device link | No stable WEC device ID; AD computer GUID/SID absent; host address is not an immutable identity |
| Domain | AD context/domain name, FQDN and DN | Windows HardwareSnapshot does not contain a computer-domain identity section |
| Hardware | CPU, RAM, disks, NIC/MAC/IP, GPU and monitor identification | No system/chassis serial, system manufacturer/model or BIOS UUID in WEC Inventory; monitor serial is a different object's identifier |
| Windows | OS caption/version/build/architecture, capture timestamp and software | Separate snapshot and scan lifecycles; not continuous telemetry |
| AD | Enabled, description, DN/derived OU, replicated last logon, OS | Computer group membership and immutable-ID profile lookup absent; missing UAC currently defaults to enabled in ComputerSearchService |
| Entra | Trust type, IDs, enabled, approximate last sign-in, registered owners through a separate read | Registration date, device on-premises SID and sync flags are not selected; don't fabricate “Joined” from a name candidate |
| Intune | Compliance, management state, enrollment type, last sync, manufacturer/model/serial, userId/UPN | Explicit primary-user relationship is not queried; enrolled-at/last registration data not selected |
| Users | Stored named interactive domain-account evidence; SID/profile observations for reverse lookup; Graph owners/registered devices; Intune associated user | No physical ownership/asset assignment; client-to-AD-user link by GUID not currently available |
| Security | Firewall, Defender, SMB1, BitLocker, TPM, OS support, patch age, local administrators; local-only registry/account-policy checks; KSC and Nessus context | Intune compliance is already available, but not individual compliance-policy causes, Defender for Endpoint, Conditional Access or Entra risk |
| Activity | Individual source timestamps and Security history | No unified logon history; no meaningful universal “last seen” field |

[S05, S06, S07, S08, S09, S16, S18]

### A4. User and group availability

AD users have immutable `objectGUID`, SID, sAMAccountName, UPN, mail,
employee ID, department/title, manager DN, DN/OU, nullable enabled state,
creation/expiry, replicated last logon, password dates and direct memberOf
groups. Entra users add cloud object ID, user type, office, cloud enabled state,
on-premises SID/immutable ID, assigned SKUs and separate optional report data.

The current AD profile does not contain a Graph facet. React mounts a separate
`Microsoft365ContextPanel` which matches the cached Entra Users collection.
Licenses, service plans, cloud groups and registered devices open in the M365
workspace. Intune `userId` links to the Graph user view, but User 360 does not
compose that inverse relationship. [S07–S12]

AD direct groups are only `{ distinguishedName, name }`; privileged browsers
add a SID-validated group context but return members without GUID/SID. General
group listing, generic AD group detail/member lookup and hybrid group matching
are missing. Graph group records have neither an on-premises SID nor sync
attributes in the selected projection. A unified Groups workspace therefore
needs a bounded read feature extension, not just moving existing tabs. [S07, S09, S13]

Direct membership must remain labelled direct. Current data does not provide a
complete effective-access model, nested expansion, primaryGroupID reconstruction,
hidden membership audit or a full directory-role assessment. A privileged AD
allowlist and MFA registration are useful evidence, not a full access verdict.

### A5. Existing relationships and navigation

| Direction | Current behavior | Gap |
| --- | --- | --- |
| AD user → WEC device | Exact SID matches stored interactive/profile evidence; link to Client 360, Inventory, Health, Security and Nessus | No cloud device/Intune facets in the same relationship list |
| WEC device → observed AD user | Name, SID evidence, time/type/confidence shown | No direct User 360 link; provider has SID, while route requires GUID |
| AD user → Entra user | Cached SID match, or explicitly labelled UPN candidate | Separate cloud profile; no common object route |
| Entra user → AD user | No inverse resolver/navigation | Dead end for administrators who start in Graph |
| Entra user → group → member | Internal cloud links work; child resource can link back to parent | Parent profile/list context and filters are not retained consistently; AD groups remain separate |
| AD user → AD group | Text name/DN and general “Open Active Directory” link | Cannot open that particular group |
| Privileged AD group → user/computer | Paged identity disclosure, DN copy | No immutable-ID link to User/Client 360 |
| Entra device ↔ Intune record | GUID relationship in cached context | Intune list has no independently addressable managed-device profile or reverse device link |
| Intune record → Entra user | Existing Graph userId link | Associated user is not the primary-user relationship |
| User → licenses/plans | Graph user detail → licenseDetails | No common User 360 license section; no SKU-to-assignees drill-down |
| Finding / cleanup → client | Existing allowlisted host deep links | Same weak host key and uneven source coverage |
| Client → Nessus findings | Existing `tab=findings&asset=...` link | Filter remains based on normalized names, not a proven object match |
| Patch / network / printer rows → computer | Mostly specialist views or observations | No common object resolver; some relations should remain typed associations |

[S03, S08, S10–S15, S19–S21]

### A6. Search, filtering and data lifetimes

| Surface | Search / filter / paging | Lifetime and refresh behavior |
| --- | --- | --- |
| Clients | Backend filtering over request-cached source snapshot; posture/source/search/sort; 50 default, max 100 rows; OS/site mode pages whole groups | Selected list/history state retained on return; cold load may read configured management inventories; manual scan/probe |
| Users | Live LDAP-backed paged search by name/account/UPN/employee ID, account status/department/base DN, stable sort; max 100 | Endpoint preference stored, not account data; profile reloads AD on open and composes stored device evidence |
| AD | Preview search of loaded examples plus independent server-paged rule/member browsers | Explicit analysis; legacy view/form/overview/hygiene restored from localStorage |
| M365 | Search/sort within fetched bounded collection; 50 UI rows per page; collection cap normally 2,000 across 20 Graph pages | Cache fresh 10 minutes, retained 1 hour, max 32 entries; stale navigation reuses data until explicit refresh; expiry can cause a new read |
| Global search | Navigation, saved targets, stored Inventory/Security hosts; debounced AD user/computer search; 6 displayed results per entity category | No Graph/group/license object search; “Microsoft 365” only as a navigation result |
| Vulnerabilities | Backend asset/finding pages; search, severity and asset filter; selected plugin detail has instance paging | SQLite current inventory and daily history; regular provider can trigger background sync; stored-only seam does not |
| Patch | Paged opsi client/product states, depot/search/status filters; Winget catalog search | Connection/depot snapshot plus persisted limited overview; package checks have their own interval |
| Print | Filter/sort/group physical rows; details/disclosures; workflows have separate scope | Server scan history, latest client-printer snapshot, selected identity carry-forward marked with older time |
| Network | Explicit target/range scan with DHCP option; result view | Observation held by active UI; no central computer identity store |
| Action Center / Cleanup | Bounded backend filtering, sorting, paging | Computed source evidence; review marks remain session-local |
| Reports | Target selection and readiness, HTML/JSON export | Latest stored Inventory/Security; no implicit scan |

Important implementation distinctions:

1. `ScanTarget.CacheKey` preserves the full uppercase execution address.
   Clients/search/hygiene often discard the domain suffix. These key schemes
   do not have equivalent behavior.
2. User-to-device composition reads every stored Inventory payload to find SID
   evidence, then loads software/Health/Security for the selected devices. This
   is local N+1 work, not a Graph issue. Extending it to every list row would
   multiply the cost.
3. The hygiene cache has one connection-request fingerprint and no TTL. The
   frontend EnvironmentContext also retains a full result; paged Clients has
   a separate view lifecycle. Refresh propagation is not universal.
4. AD UserProfile has no source-read timestamp in its DTO. A merged page cannot
   honestly stamp all its AD facts with “last Graph sync”.
5. Graph context currently provides one time/stale flag for multiple facets.
   A missing Intune cache marks device context stale even when Intune was not
   enabled. It cannot express independent Entra vs Intune coverage adequately.
6. User correlation uses the Users collection; reading one cloud user detail
   alone does not make that account discoverable to AD User 360 correlation.
7. Several old DTOs collapse distinct states: computer UAC absence becomes
   `true`; AD account expiry “never” and unavailable dates become `null`;
   optional absent monitor capture may become an empty collection.
   Metadata cannot recover distinctions already lost during mapping.

[S04, S06, S07, S08, S11, S19, S22–S24]

## B. Prioritized fragmentation and trust problems

Priorities describe what must be resolved before expanding consolidation.
They are source-code findings; no claim is made about how many production
objects are affected.

| ID | Priority | Finding and consequence | Required response |
| --- | --- | --- | --- |
| C01 | P0 before wider identity reuse | Short-hostname merging can combine `PC01.site-a.example` and `PC01.site-b.example`. `ClientKey` also turns both `10.1.2.3` and `10.8.9.10` into `10`. Some duplicate groups choose first/latest and hide other records. | Preserve source references and all candidates before aggregation. Full address normalization and namespace-aware IDs; no weak match as an execution target. |
| C02 | P1 | Full-address snapshot keys and short-name list keys disagree. A list can say scanned, while the selected FQDN opens no matching stored snapshot. Short-name local detection can also confuse another-domain namesake with this machine. | Separate lookup address, stored subject and identity. Resolve exact local aliases; never decide local execution from an arbitrary short-name match. |
| C03 | P1 | Cloud profiles and AD/WEC profiles are separate; lists do not include all source-only entities. Nessus-only/Security-only coverage is inconsistent across workspaces. | One object entry/profile model with source-only records retained; explicit inventory coverage. |
| C04 | P1 | WEC→Entra device correlation lacks stable evidence; generic AD groups lack stable keys and member navigation. | Stage required ID projection extensions before promising confirmed consolidation or group parity. |
| C05 | P1 | Missing, unqueried, empty, permission-limited and stale values are not uniformly represented; several mappings already erase these distinctions. | Source/facet metadata plus narrow upstream mapping fixes; unknown must not become enabled/zero. |
| C06 | P1 | Refresh invalidation is uneven. Report revision is wired after Inventory/Security, but Overview only reloads on host change or its own button. Graph context has no shared revision propagation. | Owner-driven, subject-scoped invalidation of stored summaries and affected relations; preserve prior data only for the same identity/session. |
| C07 | P1 | Dead ends at observed users, AD groups/members and cloud-to-on-prem navigation; Intune detail not independently addressable. | Typed relationship links and scoped object routes; preserve return context and show unresolved targets explicitly. |
| C08 | P1 | A generic combined “account active”, “healthy”, “last seen”, “primary user” or group count would conceal different meanings. | Keep independent source facts; derive only explainable summaries with coverage. |
| C09 | P2 | Global search omits cloud entities/groups and uses a separate host merger; large AD pages and bounded cloud collections cannot simply be concatenated for global paging. | One result resolver and a declared bounded working-set/federated-search policy. |
| C10 | P2 | Client composition lives in EmployeeLifecycle; API models, domain records, resource union DTO and UI matching are mixed at different seams. | Extract only concrete read ownership; do not create a universal CMDB or generic resolver framework. |
| C11 | P2 | Multiple caches/auth contexts, including persistent AD view cache and ephemeral Graph cache, have different retention/invalidation policies. | Preserve policy boundaries; never persist the combined profile in existing generic viewCache. |
| C12 | P2 | Existing Inventory reverse matching is per-host payload scanning; grouped Clients pages can render many rows despite “page size”; Graph's single gate also serializes unrelated cached/status work behind requests. | Batch bounded stored projections and measure list/profile latency before adding more consumers. |
| C13 | P2 | Historical docs can look current although superseded: UX findings and old Patch workflows are examples. | Use code plus accepted later ADRs; do not restore retired rollout/employee features during navigation migration. |

[S03–S09, S11, S12, S19, S22, S23]

### What should deliberately remain separate

- Account identity versus a human person: one administrator can have ordinary,
  privileged and guest accounts. No HR authority is integrated.
- Device registration/enrollment versus physical asset: reinstalls, shared
  devices, VMs and multiple registrations prevent a universal 1:1 promise.
- Group identity versus effective permission: direct membership is not an
  exhaustive access evaluation.
- Windows-installed software versus an opsi product versus a Microsoft SKU:
  link their relevant contexts, never equate them from a similar display name.
- Printer device versus print queue/connection versus print server.
- Source outage versus device failure, and Intune compliance versus overall
  WEC security posture.

## C. Proposed information architecture

### C1. Product rule

The administrator selects an **object**, then inspects its source-backed
facets and relationships. Confirmed source records can share a profile.
Unconfirmed candidates remain adjacent evidence with their own identity.
A composed profile does not rewrite or replace any source-owned record.

Use “Devices” as the visible successor to Clients: Graph/Intune can return
non-Windows devices as well. Windows-specific actions remain limited to an
explicit Windows execution target. Do not silently drop other cloud records or
try WinRM against them. [Microsoft device resource documentation](https://learn.microsoft.com/en-us/graph/api/resources/device?view=graph-rest-1.0)

### C2. Recommended main navigation

Keep a grouped sidebar rather than replacing all operational workspaces with
large new container pages.

| Group | Destinations | Placement decision |
| --- | --- | --- |
| Work | Overview; Action Center | Dashboard summaries and actionable findings remain distinct |
| Objects | Devices; Users; Groups | The three canonical entry points |
| Operations | Software & licenses; Vulnerabilities; Print Management; Network Scan; Reports | Distinct administrative tasks remain independently usable |
| Administration | Data sources; Settings; Error log | Source diagnostics/session state; saved settings; application troubleshooting |

This yields 13 destinations versus 14 today. The benefit is mainly elimination
of competing object entry points, not maximizing the number of removed labels.

- Device Cleanup moves inside Devices and remains directly reachable from
  Action Center and a device profile. Compare remains a Devices action.
- Software & licenses contains **Packages**, **Client versions**, **Licenses**
  and the existing **Winget packages / History** workflows. Tenant license
  capacity is fleet-level; a user's license details stay in that user's profile.
  Do not build a common product/licensing identity engine.
- Data sources contains the existing **Active Directory analysis** and
  **Microsoft 365 connection/tenant status**. It also exposes links/status for
  configured KSC, opsi and Nessus where existing APIs provide them.
  AD hygiene, DC information and privileged-group analysis survive here.
- M365 Users/Groups/Entra/Intune lists become source-filtered links to the
  canonical object workspaces after parity. Source names remain visible
  on facts, filters and diagnostic views.
- Settings retains saved identifiers, flags, thresholds and credentials.
  Data sources owns connection/session actions and source-read status;
  it must not create a second configuration editor.
- No new visual system. Reuse DataTable, Card, semantic badges, details,
  errors/loading components, the existing responsive shell and lazy routes.

### C3. Four information levels

| Level | Purpose | Content |
| --- | --- | --- |
| 1: Object list | Find and triage | Name/account, scope, compact source presence, key status/coverage, relevant relationship count; configurable filters and stable paging |
| 2: Object overview | Answer the administrator's immediate questions | Identity summary, separately stated AD/cloud state, latest relevant observations, most important findings, groups/licenses/device summary and links |
| 3: Focused details | Understand the relevant domain | Hardware/software, identity/management, access/groups, device relations, compliance/security, licenses, activity |
| 4: Evidence details | Explain or troubleshoot a displayed result | Full IDs/DNs, original status, source/query scope, retrieval and event times, matching rule and conflicting values; no tokens/raw credential payloads |

Do not reproduce every field in the overview. Compact source labels and times
are always visible; detailed matching and raw identifiers can use disclosures.
If a detail read fails, the error is shown in that visible section without
replacing unrelated successful sections.

### C4. Devices list and profile

**List:** name with short scope subtitle; source badges AD/Entra/Intune/WEC;
OS; management/compliance summary where available; evidence freshness; finding
summary; association indicator. ID conflicts get an explicit label. Do not add
a column for every backend field.

**Filters:** text/name/FQDN/available IDs; OS; AD/Entra enabled state separately;
Intune compliance/management state; source coverage; unresolved/ambiguous
identity; current hygiene filters; saved/scanned; optional OS/site grouping.
Site derived from naming stays labelled as a naming convention, not location
truth. Missing-source filters must mean “not present in the covered inventory”
or “not evaluated”, as applicable.

**Profile:** a compact overview plus these focused areas:

1. **Identity & management:** execution address, scope and source IDs;
   AD identity/state/OU; Entra registration/trust/state; Intune management and
   sync; KSC/opsi management context. Source-labelled sections inside one area,
   not four new main tabs.
2. **Inventory:** current hardware, Windows OS/build/architecture, installed
   software and explicit BitLocker query. Preserve every existing field.
3. **Health & security:** existing Health and Security subviews, check coverage,
   history/diff and Nessus context. Intune compliance is adjacent with its own
   label and time, never the overall Security result.
4. **Relationships:** observed users, registered owners, Intune associated
   user, and group relations only where captured/implemented.
5. **Event logs:** existing presets and detailed messages.
6. **Printers:** existing installed printer connections.

Together with Overview this is seven primary detail areas. Report export and
Cleanup are contextual actions with existing in-app content, not new windows.
Old section URLs keep working, including `section=diagnostics`,
`security`, `reporting` and `microsoft365`.

If there is no stable WEC–cloud join, the overview says “Cloud identity not
confirmed” and lists candidate registrations. It must not show a candidate's
Intune compliance as the Windows machine's own established compliance.

### C5. Users list and profile

The primary object is an **account**, labelled “User” in the UI. Ordinary,
administrative, guest and independent cloud accounts are not automatically one
person.

**List:** display name, account/UPN, directory/tenant context, presence of AD and
Entra facets, separate account states, department, license summary and relation
coverage. Source-only accounts remain discoverable. Before coverage is adequate,
say “No confirmed AD relation”, not “Cloud-only”.

**Profile areas:**

| Area | Current evidence to consolidate |
| --- | --- |
| Overview | Identity/contact summary, AD and Entra states, direct-access warning, licenses and device relations with coverage |
| Identity & status | AD GUID/SID/account/DN/OU/manager DN/lifecycle; Entra ID/UPN/type/office; differing org/contact values |
| Access & groups | AD direct groups and allowlisted privileged groups; Entra direct groups; same group opens canonical group detail |
| Devices | SID observations, Entra registered devices and inverse Intune userId associations with explicit relationship labels |
| Licenses | Assigned SKUs, disabled plans, licenseDetails plan states; tenant capacity link where available |
| Activity | Existing AD lifecycle/replicated last logon and optional Graph sign-in/MFA-registration evidence; not a reconstructed event history |

Leaver review is a contextual action and remains a read-only checklist. Its
current AD-only scope must remain stated until a separately tested cloud
evidence extension is added. A cloud-only account must not inherit an
AD-specific checklist that incorrectly marks absent AD attributes as completed.

### C6. Groups

Add a canonical Groups list and detail after the necessary bounded AD read
extension exists. Start with source-native groups, never name-based hybrid
group collapse.

- List: name, source/scope, object type, membership type, mail/security flags,
  member-count coverage and privileged-context indicator where evaluated.
- Overview: group identity/purpose fields actually provided, type and source.
- Members: direct members with typed links for users, devices and groups;
  limited-information objects remain visible by type/ID.
- Source details: AD DN/GUID/SID after the extension, Entra object ID,
  dynamic rule and processing state; separate counts/timestamps per source.
- An unresolved AD DN opens a resolution state or existing DN disclosure,
  not a guessed search result.
- Nested members, primary-group membership, hidden Graph members and effective
  authorization are explicitly outside the initial member-list claim.

### C7. Relationship navigation and acceptance journeys

Every link resolves a typed source reference. Keep entity identity and
relationship type separate. Opening an observed device does not imply ownership.

1. User → Devices → device profile → Identity & management shows that same
   device's confirmed facets; Back restores the user section.
2. Device → observed user → exact SID resolution → user profile → Groups /
   Licenses; no lookup by display name.
3. Group → direct member → user/device/group profile; a missing target keeps
   the group's member list visible and offers retry/source details.
4. Cloud-only Intune/Entra record → device profile works without a local host;
   no Windows scan or local-target fallback occurs.
5. User → SKU → tenant license overview retains the selected SKU. Reverse SKU
   → assigned users may filter already loaded assignment evidence, but must
   say that it is partial until an authoritative query exists.
6. Action Center → finding → device/security/source detail → return restores
   severity/source/search/page and focus.

Use one allowlisted frontend route builder for typed destinations. Reuse the
backend Action Center mapping pattern; never treat provider text as a URL.
Return context belongs in router history state. A deep link must still work
without that state and without a previously open list.

## D. Proposed technical and logical model

### D1. Existing architecture is adequate

The architecture supports composition already: ClientOverviewService,
UserManagementService, Reporting and Action Center use narrow Core projections.
No new server, repository framework, event bus, CQRS library, graph database or
universal asset entity is justified.

The actual UI uses React feature components, hooks and presentation models,
not C# ViewModel classes. Bridge DTOs are generated from C# and React maps them
into table/section/relationship presentation. Keep Graph SDK classes in
Infrastructure and LDAP attributes in the AD data-owning module.

Not all providers physically live in Wec.Infrastructure today: KSC's adapter
is in EmployeeLifecycle; Nessus's adapter lives inside its own module's
Infrastructure folder. The dependency boundary is the Core seam and module
ownership, not a requirement to move every API class in this task.

### D2. Logical composition

~~~mermaid
flowchart LR
  W[Stored WEC snapshots] --> WP[Inventory / Health / Security Core projections]
  A[AD search-only reader] --> AP[AD-owned identity and group projections]
  G[Graph SDK and MSAL] --> MC[Microsoft365-owned session cache]
  K[KSC / opsi / stored Nessus] --> MP[Concrete management projections]
  WP --> D[Device profile composition]
  AP --> D
  MC --> CP[Concrete cached cloud projections]
  CP --> D
  MP --> D
  AP --> U[User profile composition]
  WP --> U
  CP --> U
  AP --> GR[Group profile composition]
  CP --> GR
  D --> UI[Typed bridge and focused React views]
  U --> UI
  GR --> UI
~~~

Logical data ownership:

| Model | Responsibility | It must not become |
| --- | --- | --- |
| Source reference | Object kind + source + authority/tenant/directory scope + immutable source ID, or explicitly weak WEC host reference | A credential, physical-asset ID or universal mutable name |
| Match result | Confirmed / candidate / ambiguous / conflict / unresolved, rule and evidence | A score that silently authorizes a merge |
| Device profile | Header, matching evidence and separately typed WEC/AD/Entra/Intune/management facets | A flat object with one “Enabled”, “OS”, “Owner” and “LastSeen” |
| User profile | One account identity and independently sourced AD/Entra/access/license/device/activity facets | A personnel/HR master or merged collection of all accounts belonging to a person |
| Group profile | Source identity, group facts and typed direct members | A complete effective-permission graph |
| Relationship | From/to source references, relationship kind, source, observation time, evidence confidence | An ownership claim derived from a name or local profile |
| Source/facet state | Retrieval status, data presence, freshness, coverage and safe error | Business health or a replacement for the original provider state |

A small shared metadata record is useful. A generic `Dictionary<string, object>`
entity, universal `IEntityRepository<T>` or automatic provider-discovery layer is
not. Source DTOs remain separate and projection contracts include only fields
used by consumers.

### D3. Object references and routing

Proposed explicit route forms:

- `/devices/ad/:directoryScope/:objectId`
- `/devices/entra/:tenantId/:objectId`
- `/devices/intune/:tenantId/:managedDeviceId`
- `/devices/wec/:profileScope/:subjectKey`
- `/users/ad/:directoryScope/:objectId`, `/users/entra/:tenantId/:objectId`
- `/groups/ad/:directoryScope/:objectId`, `/groups/entra/:tenantId/:objectId`

These are proposals, not implemented URLs. Segments are encoded and validated.
A scope is a directory/tenant identity, not the currently selected DC hostname
and never a credential fingerprint. A WEC subject key initially identifies the
exact existing stored target; it remains explicitly weak across rename/reimage.

Do not invent a persistent global WEC GUID merely to route a profile. The
incoming source reference remains the stable anchor of that navigation. Exact
matches enrich it with other facets. They do not cause the route to change when
a cache expires or a source disconnects. When both AD and Entra entry points
resolve the same account, both render the same composition logic while retaining
their valid source anchor. A bounded in-session index can deduplicate confirmed
equivalents in lists/search. No permanent alias registry is needed initially.

Old host routes resolve exact stored targets first. An ambiguous name opens a
candidate chooser and must not select the first row. Old unscoped user/cloud
links use the applicable active context only with visible scope and validation;
they must not silently switch to another tenant/directory. Missing context
leads to a visible source-selection state.

### D4. Concrete component changes proposed for later

| Owner | Proposed change | Why this seam |
| --- | --- | --- |
| Core | Small source-reference, match-evidence and source-state contracts; concrete cached M365 user/device/group projections | Allows cross-module composition without passing Graph SDK models or credentials |
| ActiveDirectory | Extend computer projection with GUID/SID; nullable status; targeted identity resolution by GUID/SID; bounded general group read contracts | AD owns parsing, allowlists, directory connection and paging |
| Inventory | Narrow batch stored-identity/user-evidence projections; optionally extend a future explicit snapshot with approved device identity evidence | Fixes N+1 without a second independent persistence owner |
| Microsoft365 | Implement `IMicrosoft365UserContextProvider`, `IMicrosoft365DeviceContextProvider` and group projection when needed; expose per-query metadata and session revision | Consumers reuse the existing cache instead of calling IMicrosoft365Reader directly |
| EmployeeLifecycle | Preserve hygiene/source assessment initially; expose a concrete cached management read projection; retain old bridge names | Prevents reimplementation of existing findings and accidental source refresh |
| Clients (proposed module) | Later extract existing ClientOverviewService/workspace composition, then add device profile composition | Gives an actual owner to the canonical workspace; source adapters and frozen lifecycle tables do not move with it |
| UserManagement | Extend profile composition and source-aware list/search; add exact inverse resolution and cached cloud/device facets | Existing AD-authoritative account composition remains the base |
| GroupManagement (proposed module) | Small list/profile/direct-member composition once AD and cloud projections are available | A concrete new entity feature, not a generic relationship platform |
| Host | DI registrations, options, compatible bridge routing/timeouts and contract generation | Composition root remains the only project referencing all modules |
| Frontend | Typed entity route helpers; source metadata presentation; focused profile sections; later sidebar/source workspace and unified list/search | UI owns presentation and navigation, not identity joins or provider I/O |

Suggested names are deliberately narrow. Do not create all interfaces/modules
upfront. Introduce each only in the phase with its first consumer. Keep the
existing `Microsoft365Data` resource union for source-query compatibility;
don't spread it into the new Device/User profile contracts.

### D5. State, provenance and conflicts at field level

Use three time concepts instead of one “last updated”:

- **RetrievedAtUtc:** when WEC successfully read that source or snapshot.
- **ObservedAtUtc / source event time:** when the fact was measured or the
  underlying activity occurred, if available.
- **LastAttemptAtUtc + last refresh result:** whether a newer read failed.

Retain source, authority, native object reference, query scope and coverage.
A derived field also records the rule and supporting evidence references.
For most data, section metadata plus field-level exceptions is sufficient;
a verbose metadata wrapper on every primitive would be unnecessary.

| Condition | Presentation |
| --- | --- |
| Successful field with false / 0 | Display false / 0 |
| Successfully queried empty collection | “No direct groups returned”, with scope/coverage |
| Provider returned null / omitted field | “Not provided by source”; “Not available” if the adapter cannot distinguish further |
| Source/facet never queried | “Not queried” with an explicit load action |
| Optional Intune/reports disabled | “Not enabled”, not stale/error |
| Permission/consent/role unavailable | Safe specific reason and link to Data sources; other facets remain usable |
| Cached success + failed refresh | Keep prior facts with their original time; show stale/refresh error nearby |
| Truncated / permission-limited / scoped set | “Partial”, loaded count and declared total if known; never infer absence beyond scope |
| Source record not returned by an exact authorized read | “Not found in this source/context”; no global deletion conclusion |
| Conflicting exact identity evidence | Prominent identity conflict; no auto-selection, dependent actions disabled |
| Source clock in the future | Time shown with unknown freshness; no negative age |
| Local-only / cloud-only operation not applicable | Explicit “Not applicable”; do not call an unrelated local fallback |

Preserve existing SemanticStatusBadge dimensions. Add evidence coverage/matching
labels beside them, rather than putting all concepts into one status enum.

### D6. Cache, refresh and synchronization policy

Keep data ownership and retention where they exist:

| Owner | Current storage | Consolidation policy |
| --- | --- | --- |
| Inventory | Latest JSON snapshot per exact host in SQLite | Retain; additive optional fields only after approval; old snapshots remain readable |
| Health / Security | Persisted runs/history and coverage | Retain; no rewrite of historical identities or findings in the initial migration |
| AD | Live reads plus existing frontend analysis view cache | Add bounded in-session identity projections only as needed; no full persistent user/group mirror |
| M365 | Session cache: 10 min fresh, 1 h retained, 32 queries | Keep settings/limits configurable; cloud facets must never enter SQLite or localStorage |
| Hygiene | Request-fingerprinted process snapshot | Reuse explicitly; add revision/coverage, don't silently equate it with current endpoint health |
| Nessus | Current snapshot, status and daily history in SQLite | Use stored-only provider in object overviews; source sync remains owned by VulnerabilityManagement |
| opsi | Connection/depot snapshot and stored metadata | Read-only summaries use existing cache; package actions remain separate |
| Combined profiles and index | Proposed bounded process memory only | Disposable composition; no additional personal-data history or persistent identity registry |

Operational rules:

1. Object overview reads available stored/cached facets without starting remote
   scans, Ping/WinRM or a full tenant inventory. Preserve the existing bounded
   AD identity read behavior where that route explicitly requests an AD user.
2. An explicit “Load source data” or selected relationship read can fetch a
   bounded source query. State which source and scope it reads. A selected
   cloud object by known ID should not require loading all tenant users first.
3. Explicit refresh targets one source/facet. A compound refresh, if later
   necessary, lists participating reads; it never silently starts scans.
4. Source owners publish a monotonically changing in-session snapshot revision
   through their returned projections. The profile reloads affected cached
   projections after a completed scan/read. Start with React callbacks/query
   invalidation and concrete contracts, not a backend event bus.
5. Cancel on subject/scope/session change, ignore late responses, and clear the
   previous object's visible data immediately. Preserve stale results only for
   the same subject, authority and authorized session.
6. Disconnect or tenant/account/directory-context change invalidates composed
   cloud facets, indexes and matches as well as the owner's cache. Stale cloud
   data must not outlive the existing retention rule via a second UI cache.
7. A successful refresh of one source does not update another source's timestamp
   or make the whole profile “fresh”. Intune-disabled and Intune-failed are
   distinct from Entra freshness.
8. Keep requests single-flight at the concrete query seam. Do not load each
   device's licenses/groups/owners while rendering a list. Use bounded batch
   projections for local stored evidence; retain Graph throttling/cancellation.
9. Rendering a cached field after its age threshold must update the freshness
   label without automatically querying its source.
10. No delta synchronization, background directory crawler, persistent cloud
    index or new retention policy in the initial consolidation.

### D7. Unified list and large-environment strategy

Do not merge page 1 of AD with page 1 of Graph and call it one globally sorted
inventory. Their paging, counts, visibility and snapshot times differ.

Deliver profiles first. Then build a bounded, process-local **working-set
index** of minimal source references and list fields, populated through explicit
source loads and existing caches:

- AD list projection remains owned by ActiveDirectory and uses bounded paging;
  it does not build every user's full profile or fetch per-user memberships.
- Graph contributes its bounded selected collection and coverage manifest.
- WEC stored hosts, saved targets, Security-only and standalone Nessus records
  can enter the working set without being auto-merged by name.
- Match and deduplicate only verified equivalence before filtering/sorting/
  paging that working set. Use a fixed revision for a browsing session and an
  explicit “New data available” refresh to avoid rows shifting mid-page.
- Return `loadedSourceRecords`, `confirmedObjectCount`, unresolved candidates,
  per-source totals if known, and coverage. A combined total is exact only
  for the working set, not necessarily for the environment.
- A cloud record without a match in incomplete AD data is “AD relation not
  established”, not proven cloud-only.
- If the configured memory/item bound is reached, show partial coverage and
  offer a bounded targeted search. Do not silently raise Graph caps.
- Global search may show separately labelled source results until exact matches
  are resolved. It uses the same reference resolver as profile links and never
  triggers full inventory loads per keystroke.
- A future requirement for complete large-directory counts, persistent offline
  search or multi-tenant coverage needs its own measured design decision.
  It is not implied by placing data in a common table.

This makes the limitations explicit while avoiding an expensive universal
synchronization subsystem before the profiles prove useful.

## E. Identity matching strategy

### E1. Preconditions and result states

Identity resolution has to be deterministic and explainable. Validate type,
source authority and identifiers before comparing values. A GUID from AD, a
Graph directory object ID, a Graph deviceId and an Intune managed-device ID are
different identifiers even when they all look like GUIDs.

Match results are:

| State | Meaning | Consequence |
| --- | --- | --- |
| Confirmed | An exact, semantically valid identity relation is supported in the relevant scope | Compose the linked facet; retain the underlying IDs and rule |
| Candidate | A plausible alias or weak identifier matches | Show adjacent evidence; do not collapse records or inherit risk/ownership |
| Ambiguous | More than one possible counterpart or incompatible enrollment record | Show all bounded candidates; select none automatically |
| Conflict | Strong evidence disagrees | Stop this join; weaker matches cannot override it |
| Unresolved | Insufficient data, no match, or the relevant source was not read | Show coverage/reason; never equate with deleted or nonexistent |
| Source-only confirmed | No counterpart within a sufficiently complete, applicable scope, or explicit source facts establish it | Keep a fully usable standalone profile; no missing-source penalty by default |

“No match among loaded rows” alone never establishes source-only status.
A stale confirmed identifier can still support identity, but its associated
business state remains stale. A partial query cannot establish global uniqueness.
For SID-based account joins, a later targeted bounded lookup can establish
uniqueness without loading the entire tenant; that typed query is not in the
current Graph contract and must be added and tested deliberately.

Never use automatic transitive closure over candidate edges: if WEC is a name
candidate for Entra, and Entra is ID-linked to Intune, WEC is still only a
candidate for those cloud facts.

### E2. User matching priority

| Priority | Rule | Available now | Required handling |
| --- | --- | --- | --- |
| U0 | Same `directoryScope + objectGUID` or same `tenantId + Entra object ID` | IDs exist; route scope is incomplete | Same source account; handles rename without identity loss |
| U1 | AD objectSid equals Entra onPremisesSecurityIdentifier | Yes; current M365 policy uses this | Confirm the account relation only in the configured directory/tenant scope, with valid IDs and unique applicable result; preserve exact evidence |
| U2 | Explicitly verified Entra sourceAnchor mapping | Entra immutable ID and AD GUID exist; actual sourceAnchor/configured AD attribute not captured | Deferred. Never assume base64(AD GUID) is correct; verify attribute, encoding and case rules before enabling |
| U3 | Exact normalized UPN | Yes, current policy labels Candidate | Candidate only; do not join if SID evidence conflicts; do not rewrite guest UPNs |
| U4 | Mail, display name, employee name or department | Some values present | Search hints only; no automatic identity or person merge |

Microsoft documents onPremisesSecurityIdentifier as the synchronized user's
on-premises SID. SourceAnchor selection is configurable and encoding matters.
[Microsoft user resource](https://learn.microsoft.com/en-us/graph/api/resources/user?view=graph-rest-1.0),
[Entra Connect sourceAnchor design](https://learn.microsoft.com/en-us/entra/identity/hybrid/connect/plan-connect-design-concepts).
The implemented conservative SID-first rule is therefore the appropriate
starting point for WEC.

Further rules:

- SID comparison is not SIDHistory inference. SIDHistory is not collected and
  is not an automatic fallback.
- Same person's admin/user accounts remain independent identities.
- Recreated AD accounts with the same UPN/name but different GUID/SID remain
  different; a strong mismatch blocks UPN fallback.
- A guest or unsynchronized account remains its own tenant-scoped account.
- Do not perform cross-tenant or cross-forest matching merely because a string
  SID/UPN matches. The applicable authority must be explicit.
- A user can link to many devices. Device observations do not prove assignment.

### E3. Device matching priority

| Priority | Rule | Current feasibility | Confidence and scope |
| --- | --- | --- | --- |
| D0 | Same source namespace and immutable object ID | Entra/Intune yes; AD computer extension required; WEC is host-addressed | Same source record, not automatically the same physical asset over reinstall |
| D1 | Intune azureADDeviceId equals Entra deviceId in the same tenant | Implemented | Confirm registration-to-management association; never compare with Entra object ID |
| D2 | WEC-observed Entra tenant/device ID equals cloud registration | Not captured | Strong future bridge if collected through an approved, bounded, trustworthy Windows identity read |
| D3 | Explicit AD computer GUID/SID relationship to a trusted Windows/cloud identity | AD computer IDs and Windows identity seam absent from current projection | Add only after source semantics are verified; a SID and device ID are never interchangeable |
| D4 | Exact, valid system serial plus manufacturer/model and compatible lifecycle evidence | Intune has serial/model; WEC does not | Candidate/supporting evidence initially; serials may repeat, be placeholders or survive reimage |
| D5 | Exact normalized FQDN within the same directory scope | AD/opsi/Nessus and stored targets partially provide it | Candidate cross-source relation; better than short name but mutable/reusable |
| D6 | Exact short hostname with compatible known domain context | Current hygiene rule; M365 uses full exact display-name candidate | Low-confidence candidate only; no automatic cross-domain collapse |
| D7 | IP, MAC, user name, model or approximate name similarity | Some observed | Search/diagnostic evidence only; never sole durable identity |

The Graph device resource distinguishes its object `id` from registration
`deviceId`. Intune's `azureADDeviceId` is the latter relation.
[Microsoft device resource](https://learn.microsoft.com/en-us/graph/api/resources/device?view=graph-rest-1.0),
[Intune managedDevice resource](https://learn.microsoft.com/en-us/graph/api/resources/intune-devices-manageddevice?view=graph-rest-1.0).

Normalization policy:

- Preserve full FQDN; remove a trailing DNS root dot, normalize DNS comparison
  case, but retain the original display value.
- Parse addresses as IPv4/IPv6 before DNS processing. Never split an IP at the
  first dot and never use reverse DNS to silently change identity.
- Keep a separate short-name alias for candidate search; it is not the key.
- Reject empty/all-zero malformed GUIDs. Keep provider ID namespaces distinct.
- Preserve serial strings exactly for display. Normalize a comparison copy
  conservatively; reject known empty/placeholders and do not strip leading
  zeros or meaningful punctuation indiscriminately.
- Do not combine an old registration with a new one because its name or serial
  is the same. Show separate records and evidence age.
- Multiple Intune IDs for one Entra deviceId remain multiple enrollment records.
  Picking the newest silently is not sufficient evidence of the active record.

A confirmed four-source physical-device identity cannot be delivered from the
current projections alone. The initial consolidation must work usefully with
source-only and candidate records. Choosing an additional Windows identity read
is a separate phase and decision, not a disguised name-based join.

### E4. Groups and other entities

AD groups need directory-scoped GUID/SID in a new projection; Entra groups use
tenant-scoped object ID. Current DN/name-only AD references and cloud group
fields do not support a confirmed hybrid group merge.

Start with separate source-native group records. A later on-premises SID/sync
extension may enable a verified relation after attribute and permission review.
Group name, mail address and “Security group” type never prove equivalence.
M365 Unified, security and distribution are classifications of group records,
not four extra copies of a group.

License identity is tenant + SKU ID, with service-plan ID beneath it. Keep
subscribedSku/assignment/licenseDetails object IDs distinct. An SKU label is
display metadata. Do not correlate installed software to SKU entitlement by name.

Nessus UUIDs require their provenance. Current parsing stores host-uuid and
bios-uuid in one field, taking the first available value. That field must not
be treated as a guaranteed immutable hardware UUID without an adapter change.
Keep Nessus asset references intact and show heuristic host links separately.

Print Management's serial → IP → base-name physical-printer grouping is
specialist behavior. Preserve its lease-history semantics; do not transplant
that grouping into computer identity resolution.

### E5. Mandatory matching examples

| Synthetic scenario | Expected result |
| --- | --- |
| AD and Entra UPNs differ, exact valid synchronized SID agrees | Confirm account relation; show different UPN values with source |
| UPN agrees, valid on-premises SIDs differ | Identity conflict; no merge |
| Duplicate SID in retrieved candidates | Ambiguous; no first-row selection |
| Graph Users collection was never loaded, but a user detail is cached | Resolve from supported cached detail/known ID or show not evaluated; don't claim user absent |
| AD scope truncated and one Entra account has no matching AD row | “AD relation not established”, not proven cloud-only |
| One AD account recreated with the same name | New account identity; no inherited old profile ownership |
| Intune Entra ID equals Graph deviceId, while Graph object ID differs | Confirm registration link |
| Two Intune records reference the same deviceId | Both records visible; no implicit latest-wins management state |
| Same short hostname in two domains | Separate records with candidates, not one row |
| Two saved IPv4 targets share the first octet | Separate exact targets |
| FQDN exists in AD; only a short-name WEC snapshot exists | Candidate alias; no false “snapshot belongs to this exact record” assertion |
| Device has no Intune read permission | Intune unavailable; not “unmanaged” or “noncompliant” |
| Intune record has a userId | Link associated Entra account; do not label it confirmed primary user |
| Primary group/nested membership was not queried | Group list explicitly incomplete for effective access |
| Monitor serial matches a cloud computer serial by coincidence | No computer match |
| Source disconnects during refresh | Cancel/ignore late result; clear invalid cloud facets and matching index |

## F. Source-of-truth and conflict policy

Authority is defined **per fact**, not once for the whole object. A source's
authority is not proof that its value is fresh.

| Fact | Preferred source / rule | What remains visible |
| --- | --- | --- |
| AD identity, SID/GUID, DN/OU, directory state | AD for that AD account/computer | Cloud object identity/state separately |
| Cloud identity, user type, registration and trust | Entra for that cloud object | AD presence and WEC linkage confidence |
| Profile display label | Label from the chosen source anchor; meaningful local OS/device name may be displayed alongside it | Alias differences with source; label never changes execution target |
| Hybrid account contact/org fields | AD preferred for AD-backed identity under current product policy; cloud-only account uses Entra | Differences in mail/UPN/department/title are preserved; no claim WEC knows the tenant's full sync-master policy |
| Current measured Windows OS/build/architecture | Latest valid WEC Windows capture | AD operatingSystem and Entra/Intune OS values with original times |
| System manufacturer/model/serial | Currently Intune when present; future trusted WEC hardware capture may be the direct measurement | Source label and age; no use of monitor identity |
| AD enabled vs Entra accountEnabled | Separate administrative states | “AD disabled / Entra enabled” can be valid evidence requiring review; not one synthetic Enabled boolean |
| Intune management/compliance | Intune managed-device record | Exact provider state, last sync and record identity; unavailable is not false |
| Device-user relation | Source owns each distinct relation: WEC observation, Graph registration owner/device link, Intune associated user | All types with time and confidence; no universal Owner field |
| Explicit Intune primary users | Not queried currently | “Not queried/not implemented”; future supported relationship read is separate |
| AD direct group membership | AD | Entra direct memberships separately; no union presented as effective rights |
| Entra group membership/dynamic rule | Entra | AD counterpart and counts only if independently supported |
| User license assignment / service plans | Graph user license data | Retrieval time, SKU/plan IDs and individual states |
| Tenant seat capacity | Graph subscribedSkus and current nullable arithmetic | Enabled/consumed/remaining separately; no guessed marketing catalog |
| Installed Windows software | WEC registry capture | opsi target/current package state as different facts |
| opsi package/target state | opsi | WEC installed entries and capture time; Winget catalog version is an upstream reference |
| WEC security condition | The relevant completed check and coverage | Intune compliance, KSC status and Nessus findings remain separate evidence |
| Nessus vulnerability condition | Imported scan/finding instance and scan time | Match confidence and source scan; “no match” is not “zero vulnerabilities” |
| Activity | Each source owns its own timestamp meaning | AD replicated logon, Entra approximate sign-in, Intune last sync, WEC capture and explicit connectivity |
| Saved target label/address | User preference and chosen execution endpoint | Not authoritative identity or ownership |
| Physical device ownership/return | No current authoritative provider | Remains unknown/manual review; do not derive from interactive user |

Graph documents a managedDevice `users` relationship for primary users, while
the current WEC query selects `userId`/UPN fields. This supports keeping
“Associated user” distinct from “Primary user”.
[Microsoft Intune managedDevice resource](https://learn.microsoft.com/en-us/graph/api/resources/intune-devices-manageddevice?view=graph-rest-1.0).

Conflict display example:

> Device names differ. WEC observed `PC-NEW`; Entra reports `PC-OLD`;
> Intune reports `PC-OLD`. Entra and Intune are linked by deviceId. Their
> retrieval and sync times remain visible. The WEC relation is shown with its
> own matching evidence.

If the WEC link is only a hostname candidate, present “Possible related
registration” rather than treating those names as facts about one confirmed
computer. The distinction precedes all field precedence rules.

Operational conflict policy:

1. Show the selected source value with a source label.
2. If comparable fields disagree, provide an adjacent difference indicator and
   inline list of the other values/times.
3. Do not create a conflict simply because different concepts differ:
   lastSync and lastSignIn are not competing values; local and cloud account
   enabled states govern different accounts.
4. Strong identity conflict blocks dependent aggregation and target resolution.
   Attribute differences on a confirmed relation do not erase either record.
5. A newer retrieval is not necessarily a newer observation. A freshly read
   replicated AD timestamp can describe old activity.
6. Never replace a known value with a fabricated fallback because a refresh
   failed. Old data remains stale with the failure visible.
7. Derived “most recent evidence” may link to the maximum relevant observation
   time, but its label must name the event/source. It is not “online now” and
   not one canonical activity timestamp.
8. New observations do not automatically override a Cleanup concern or complete
   a Leaver checklist.
9. If source adapters cannot distinguish empty from unreturned/unknown, display
   uncertainty and plan a mapping fix rather than guessing in React.

## G. Migration plan for a later explicitly started implementation

### G1. Sequence and checkpoints

The sequence intentionally improves identities and profiles before switching
navigation. Each phase should be a reviewable local slice. No phase is started
by this report.

| Phase | Goal / deliverable | Dependencies |
| --- | --- | --- |
| 0 | Approve scope, record ADR, establish current characterization baseline and feature-parity checklist | Separate implementation start and decisions in I |
| 1 | Protect source identity, local/remote target resolution and source-state semantics | 0 |
| 2 | Concrete cached composition seams, scoped source revisions and authoritative references | 1 |
| 3 | Consolidated device profile using currently available evidence | 2 |
| 4 | Consolidated user profile and both directions of device/account navigation | 2; reuses 3 destinations |
| 5 | Groups foundation and typed direct-member navigation | 1–2; 3–4 targets |
| 6 | Bounded unified lists and global search | 3–5; list-size/coverage decision |
| 7 | Sidebar/source-workspace migration and complete old-link adapters | 3–6 feature parity |
| 8 | Optional stronger Windows↔AD↔cloud device identity collection | Explicit collection decision; 1–3; can be moved earlier if chosen as an initial acceptance requirement |
| 9 | Regression, security/privacy and desktop acceptance; documentation closure | All required earlier phases |

The first independently useful milestone is **phases 0–4**: one source-aware
device and account profile with usable relationship navigation. General group
inventory and unified lists follow without forcing a big-bang rewrite.

### Phase 0 — Decisions and characterization

**Goal:** a concrete, approved implementation scope with repeatable current
behavior tests.

**Components:** route registry and tests; Clients/User/AD/M365 tests; Core
contracts; relevant ADRs and roadmap execution file.

**Changes later:** create a new `codex/...` implementation branch; record a new
proposal/accepted decision through the normal process. Fix the intended object
scope, cloud-only account policy, navigation, identifier collection boundaries
and retention. Record the parity matrix below. Characterize existing actions,
exports, nullable values, cache lifetimes and aliases before extraction.

**Risks:** accidentally treating the old UX audit as open work; reviving retired
Patch rollout or employee CRUD; redefining source-only as “healthy”.

**Tests:** full existing baseline and named architecture/contract checks; add
characterization cases for cross-domain names, IPv4 collision and exact stored
target lookup. No live provider requirement for unit tests.

**Exit:** accepted boundary and known failing identity cases isolated as
regression targets, not silently hidden by a rename.

### Phase 1 — Identity preservation and source semantics

**Goal:** stop losing distinct records before trying to consolidate them.

**Components:** ComputerSearchService / AdComputerInventoryItem;
ClientWorkspacePaging; ItHygieneService; client/search/compare key helpers;
ScanTarget consumers; relevant nullable DTOs/mappers.

**Changes later:** expose AD computer GUID/SID and explicit directory scope
through allowlisted reads; preserve native source records/candidates; add typed
references; separate full host/address parsing from short aliases; preserve
unknown UAC and available absence semantics. Fix exact local-target resolution
without moving or deleting historical snapshot rows.

Keep existing heuristic hygiene assessments visibly heuristic during the
transition. Adding safer identity information does not silently reclassify old
assessments or rewrite stored Nessus history.

**Risks:** changed list counts, duplicate exposure, old host URLs resolving
differently, local/remote execution mistakes, JSON/contract compatibility.

**Tests:** GUID/SID mapping, null fields, FQDN/domain collisions, trailing dot,
IPv4/IPv6, local machine versus another-domain namesake, duplicate records,
same-name recreated object, exact snapshot lookup; existing hygiene/compare/
saved-target/remote-credential tests.

**Dependencies:** 0; ID allowlist approval. **Exit:** no first/latest-row
selection for ambiguous identity and no source information discarded by the
new path.

### Phase 2 — Composition ownership and revision contracts

**Goal:** reusable object facets with no source-query duplication.

**Components:** Core concrete projection contracts; Microsoft365Service;
Inventory read providers; EmployeeLifecycle source cache; Host DI;
new bounded Clients composition module.

**Changes later:** extract existing Clients read composition to its intended
owner while retaining bridge aliases. Keep source/assessment logic in existing
owners. M365 exposes cached user/device and later group facets with independent
metadata and session revision. Add bounded stored batch projections for local
evidence; do not introduce generic repositories.

**Risks:** accidental module reference; consuming IMicrosoft365Reader directly
and bypassing its cache; scoped DbContext shared across parallel operations;
composed data surviving a disconnect.

**Tests:** mock Core providers; verify zero remote scan/sync on cached profile
read; metadata mapping, partial source failures, cancellation, cache eviction,
session/directory scope change and stale-response rejection; DI validation and
generated-contract check.

**Dependencies:** 1. **Exit:** object composition has a concrete module owner
and independent facet states, with no permanent cloud storage.

### Phase 3 — Device profile

**Goal:** one place to inspect an operational device or cloud record.

**Components:** ClientDetailPage/OverviewSection and current sections;
Microsoft365ContextPanel/Fields; DeviceRelationshipMap; new profile DTO and
route adapters; Inventory/Security callbacks.

**Changes later:** render current WEC, AD, Entra, Intune, KSC, opsi and Nessus
facets in the proposed hierarchy. Show candidates separately. Support an
Entra-only/Intune-only profile with no local host. Add independent source refresh
and reload stored summaries after successful scans. Preserve running scans
during tab changes and keep reports functional.

**Risks:** accidental Windows scan of a cloud-only object; stale facts on a new
subject; hidden errors; losing Security history, Events, BitLocker or Printers
during tab regrouping.

**Tests:** complete/partial/unknown/conflicting facets; disabled Intune vs denied
Intune; one source fails while other sections remain; refresh propagation to
Overview and Report; no ownership claim; route transitions/cancellation; every
old client section URL; keyboard and narrow-window rendering.

**Dependencies:** 2. **Exit:** opening any supported source reference produces a
useful honest profile without requiring four separate source workspaces.

### Phase 4 — User profile and bidirectional relations

**Goal:** AD and Entra account facts, groups/licenses/devices visible from one
account profile.

**Components:** UserManagementService/UserReadModels; IDirectoryUserReadProvider;
new exact SID lookup; UserDetailPage/UserDevicesSection; cloud projection
providers; entity route helpers.

**Changes later:** preserve AD identity authority, add Entra-only account path,
compose existing licenses/plans/cloud groups and device associations. Resolve a
client observation's SID to a directory GUID through a bounded exact lookup or
cached identity index. Add inverse Intune userId relationships from loaded
evidence. Keep UPN candidates apart from confirmed SID relations. Manager DN
can become navigable only after exact resolution.

**Risks:** combining two accounts of one person; privilege/credential context
leakage between directories; false primary-user label; Leaver review claiming
cloud coverage it does not assess; N+1 profile fetching.

**Tests:** all U0–U4 cases, SID inverse resolution/duplicates, cloud-only/guest/
disabled/missing account, truncated membership/license data, linked device
directions, preserved Leaver export/marks and source scope, cancellation and
return navigation. Explicitly test that a partial list cannot prove no license
or no AD counterpart.

**Dependencies:** 2–3 plus ADR 0019 extension. **Exit:** user→device→user works
without navigating through data-source home pages.

### Phase 5 — Groups

**Goal:** general source-aware group list/profile/direct-member navigation.

**Components:** ActiveDirectory read services/allowlists and new group Core
projection; Microsoft365 group cache adapter; proposed GroupManagement module;
Groups React workspace; directory identity/member browsers.

**Changes later:** first introduce AD GUID/SID-based group detail and bounded
member reads, then list/search. Retain four-group SID privilege allowlist and
all existing hygiene browsers. Reuse Graph group/member reads. AD and Entra
records remain separate unless verified matching evidence has been explicitly
added. Convert source/member references into typed links.

**Risks:** accidentally broadening permissions; confusing direct and transitive
membership; ranged/large AD membership handling; hidden/limited Graph objects;
display-name group merges; group cycles in navigation.

**Tests:** page boundaries and stable sorting, missing GUID/SID, invalid or
escaped DN, non-user members, nested-group links without recursive traversal,
limited-information objects, permission failures and duplicate names. Preserve
privileged-group allowlist tests and complete visible member coverage labels.

**Dependencies:** 1–4 plus approval of bounded general AD group read scope.
**Exit:** an AD user group can open its specific group, and supported members
can open their profiles without losing the list.

### Phase 6 — Unified lists and global search

**Goal:** the same objects can be found from source-independent workspaces.

**Components:** Clients and Users list services; new Groups list; minimal
source working-set projections; GlobalSearch/searchResults; DataTable/filter
state; comparison and saved-target consumers.

**Changes later:** implement D7's bounded index; include otherwise omitted
source-only records; deduplicate confirmed matches only; apply whole-working-set
filter/sort/page and expose counts/coverage. Use identical typed route resolution
from lists, search and relations. Persist only approved UI preferences; keep
cloud-derived rows/index/results in process memory.

**Risks:** false totals after merging independent pages, cloud data written by
generic viewCache, unstable pages on background source changes, expensive
full-profile requests per row, incorrect bulk-target selection.

**Tests:** multiple provider pages, identical names in different authorities,
partial/truncated sources, unknown count vs zero, source-only records, index
revision changes, reset page on filters, Back restores filters/scroll/focus,
six-result global search plus cloud/group sources, bounded memory/no N+1 calls.
Bulk actions accept explicit valid execution endpoints only.

**Dependencies:** 3–5 and working-set scale decision. **Exit:** consistent object
findability with honest loaded-scope counts; no claimed global count from partial
provider pages.

### Phase 7 — Navigation migration and compatibility

**Goal:** change entry points only after replacement workflows are complete.

**Components:** routeRegistry/App/global search; new Data sources presentation;
M365/AD wrappers; Patch navigation shell for Licenses; Cleanup entry placement;
Action Center href mapping; Reporting links; Settings section navigation.

**Changes later:** switch visible navigation to C2; retain old URLs through
allowlisted adapters. Move M365 object navigation to canonical destinations,
tenant/connection status to Data sources, SKU capacity to Software & licenses.
Keep existing AD analyses. Reuse existing feature components so moving a view
does not rewrite its data-loading logic.

**Risks:** losing deep links, changing URL parameter meaning, destroying running
batch/review state, conflating specialist actions with read-only profile actions.

**Tests:** all rows in G2; bookmarked URLs with empty router history; browser
Back/Forward and keyboard navigation; lazy loading/error boundary; filters,
selected SKU/asset/group/user, source selection, settings dirty state and all
exports. Test retained confirmed Print and Patch write gates without executing
real mutations.

**Dependencies:** feature parity through 6. **Exit:** every existing function
has a reachable successor or retained specialist destination.

### Phase 8 — Optional stronger device identity collection

**Goal:** improve WEC↔AD↔cloud confirmation where existing evidence is inadequate.

**Components:** optional typed Windows identity seam in Core/Infrastructure;
Inventory explicit-scan snapshot; AD computer projection; only the required
cloud selects/mappings; matching policy.

**Changes later:** choose a supported bounded Windows identity read that provides
tenant/device registration identity, or an independently verified directory
identity relation. Evaluate a documented native local Windows registration
API first; remote collection must be separately designed and tested under
existing WinRM/credential boundaries. Do not assume a local-only API supports
remote targets. No generic shell or arbitrary PowerShell.

System serial/manufacturer/model collection can improve hardware evidence, but
must not be sold as equivalent to confirmed directory/cloud identity. If a
reliable approved seam cannot be supplied, keep the honest candidate behavior;
it is not a reason to invent confidence.

**Risks:** unsupported remote behavior, extra personal/identity data, placeholder
serials, re-enrollment/reimage ambiguity, implicit on-open scans, expanding
stored retention without a decision.

**Tests:** native/transport seam mocked; unavailable/permission/local-only states,
old snapshot deserialization, ID validation, tenant mismatch, serial placeholder
and duplicate cases, enrollment lifecycle; company acceptance only against a
designated non-critical Windows client.

**Dependencies:** explicit collection/retention and local/remote scope decision.
**Exit:** every new confirmed join carries real captured evidence; otherwise
that relation remains candidate/unresolved.

### Phase 9 — Verification and closure

**Goal:** confirm consolidation preserved behavior and security boundaries.

**Components:** all changed source adapters/projections/UI paths; tests;
documentation and execution register.

**Changes later:** remove only replaced wrappers after verified parity; retain
compatibility routes and historical tables. Reconcile documentation with
accepted decisions and recorded outcomes. No tag/release without explicit
approval.

**Risks:** successful unit tests mistaken for real enterprise acceptance;
unavailable providers recorded as passed; installer/runtime behavior untested.

**Tests/gates:** Release build, full backend/frontend tests, contract generation
check, dependency rules, frontend production build, audit gates, diff check.
Desktop smoke at normal/narrow sizes and keyboard-only; partial/offline/session
switch/large-list examples; representative authorized AD/Graph/Intune checks;
published-host packaging through normal CI gates.

**Exit:** acceptance journeys and preservation matrix pass; unavailable external
gates and remaining candidate-only correlations are explicitly documented.

### G2. Feature and deep-link preservation matrix

| Existing route / function | Proposed destination / adapter | Required preservation |
| --- | --- | --- |
| `/` Dashboard | Overview | Existing source counts, scope and freshness; update links, no cloud-count fabrication |
| `/actions` | Action Center | Filters, severity, recommended next action, source coverage and deterministic links |
| `/cleanup?host=...` | Devices → Cleanup; keep old route | Classification, source evidence, manual decision/reason, Markdown; Excel covers full filtered selection and exactly its explicit bounded Ping workflow |
| `/clients?posture=...` | Devices list | All posture/source filters, search/group/sort/page, saved targets, connection evidence and explicit batch scans |
| `/clients/compare` | Devices → Compare | Searchable picker, recently compared targets, Inventory/Security availability and diff behavior |
| `/clients/:host` | Scoped device profile or unresolved-target chooser | Correct host, credentials and local/remote mode; no silent first match |
| `?section=inventory` | Inventory | Hardware/software/monitors/NICs, explicit BitLocker, capture errors and scan refresh |
| `?section=diagnostics` | Health & security → Health | All four checks, existing backend Diagnostics names and historical payload compatibility |
| `?section=security` | Health & security → Security | Per-check outcomes, coverage, history and comparable diff |
| `?section=events` / `printers` | Events / Printers | Presets/detail messages; client printer scan and latest snapshot |
| `?section=reporting` | Device report action/content | Same host/readiness, latest Inventory/Security, HTML/JSON and save dialog |
| `?section=microsoft365` on client | Identity & management, cloud section | Source metadata, optional Intune and candidate/ambiguous evidence |
| `/users/:objectId?section=...` | Scoped account profile | Overview/access/devices and Leaver section aliases; no AD/tenant inference from display name |
| `/users/:objectId?section=microsoft365` | User identity/cloud facets | Licenses/groups/devices/activity links remain reachable |
| `/microsoft365?resource=TENANT` | Data sources → Microsoft 365 | WAM login/logout, consent/scope display, tenant status, selected feature flags and cache clearing |
| M365 `USERS / USER` | Users list / Entra-anchored profile | ID scope, nulls, account fields and per-user links |
| M365 `DEVICES / DEVICE` | Devices list / Entra-anchored profile | Object ID vs device ID, trust and enabled state; owners |
| M365 `MANAGED_DEVICES` | Devices with Intune filter | Every managed record, inline fields, related user; new addressable record route |
| M365 `GROUPS / GROUP / GROUP_MEMBERS` | Groups list / group / direct members | Dynamic rule, direct/partial membership, type-safe links |
| M365 `LICENSES` | Software & licenses → Licenses | SKU/plan identifiers, nullable capacity and exhaustion/overassignment warnings |
| M365 `USER_LICENSES / USER_GROUPS / USER_DEVICES` | Same user profile's focused area | Preserve selected user and relationship scope |
| M365 `DEVICE_OWNERS` | Device Relationships | Registered-owner wording, no primary-user conversion |
| M365 `USER_ACTIVITY / USER_REGISTRATION` | User Activity | Optional permission/error behavior; MFA registration is not enforcement |
| `/activedirectory` | Data sources → AD analysis | Domain/DC/count/hygiene, exact-count rule pages, four privileged-group browsers, connection testing |
| `/employeelifecycle?filter=...` | Existing allowlist → Devices posture | Compatibility redirect; no frozen employee CRUD restoration |
| `/vulnerabilities?tab=...&asset=...` | Vulnerabilities | Assets/findings/scans/trend, all instance pages; adapt exact object references without losing old asset filter semantics |
| `/patchmanagement` | Software & licenses, package subview | opsi product/client reads, Winget preview/build/adopt/update, audit history; no client rollout added |
| `/printmanagement` | Print Management | Queue/device grouping, SNMP/DHCP/CCRX, history/diff/export and confirmed unused-port deletion |
| `/networkscan` | Network Scan | Explicit nmap/DHCP behavior, typed results, cancellation and no auto-asset merge |
| `/reporting` | Reports | Existing single-subject export, not a newly promised fleet report |
| `/settings?section=...` | Settings aliases retained | Existing non-secret defaults and secure credential paths; section anchors, dirty state/restart messages |
| `/logs` | Error log | Filters/details, “Hide previous entries” semantics, physical log files unchanged |
| Save/unsave client and PowerShell button | Explicit device operational actions | Saved target semantics, identity/credential scope, valid endpoint and existing launch boundary |

No deprecated source adapter, historical data table or specialist action is
deleted solely because its old sidebar entry disappears.

### G3. Test system impact

Existing test anchors to reuse:

- `ItHygieneServiceTests`, `ItHygienePagingTests` (also client workspace
  paging), `ClientOverviewServiceTests` and concrete evidence-provider tests.
- `ComputerSearchServiceTests`, `AdFiltersTests`,
  `DirectoryHygieneServiceTests`; LDAP decoding, bounded accumulator and
  error-mapper tests in Infrastructure.
- `UserManagementServiceTests`, both Inventory relationship-provider tests,
  Leaver export/review tests.
- `Microsoft365CorrelationTests`, `Microsoft365ServiceTests`,
  Infrastructure `GraphReaderTests`, Host `Microsoft365BoundaryTests`
  and M365 settings tests.
- `ActionCenterServiceTests`, DeviceCleanup service/evidence/workbook/export
  tests, StoredNessusComputerInventoryProviderTests and Nessus import tests.
- Real SQLite file migration/persistence suites for Inventory, Security,
  Diagnostics, printers, saved targets and legacy tables.
- Frontend routeRegistry/AppRoutes/GlobalSearch/searchResults; ClientsPage,
  ClientDetailPage, OverviewSection, ClientBulkActions, ComparePage,
  UserDetailPage/UsersPage/UserDevices relationships; AD identity/member
  browsers; M365 rendering; RelationshipMap, SemanticStatusBadge and
  EnvironmentContext tests.
- Specialist Reporting, Patch/Winget, Vulnerabilities, Print and Settings
  regressions remain part of the full suite even when their data logic
  is unchanged.

New meaningful test groups belong at the affected seams, not in a generic
“consolidation framework” suite:

| Group | Essential assertions |
| --- | --- |
| Identity policy | All E5 examples; strong-ID contradiction beats name; no candidate transitive escalation |
| Source metadata | Null/false/zero/empty/absent/denied/not-enabled/truncated/future-time remain distinct |
| Read composition | One facet fails; other facets remain usable; no writes/scans triggered; no credentials in profile |
| Cache/session | Cache-only read, stale refresh, identity/scope change, late response, disconnect, expiry and per-facet revision |
| List/index | Deterministic merged paging, bounds, incomplete-source totals and stable page revision |
| Navigation | Group↔user↔device journeys, cloud-only profile without WinRM, inverse SID resolution and restored context |
| Security boundary | GET-only Graph, trusted hosts, unchanged scope allowlist, no SDK model leak, no cloud data in localStorage/SQLite |
| Existing operations | Batch progress/cancel, comparison, all exports, Patch/Print confirmation and no new remediation |
| Migration | Additive optional snapshot fields, old host links, unknown legacy coverage and frozen lifecycle rows preserved |

The existing automated suite must remain tenant-free. Real AD/Intune tenant
roles, registration peculiarities and large-company data quality are separately
recorded acceptance work.

## H. Before / after at a glance

| Today | Proposed |
| --- | --- |
| Clients contain WEC/AD/KSC/opsi with Nessus enrichment; cloud devices elsewhere | Devices opens every supported source record and composes confirmed facets |
| User 360 is AD-only with an M365 context tab leading away | One account profile with identity, groups, devices, licenses and activity |
| Groups split between AD DN text, privileged browsers and Graph resource pages | Canonical source-aware Groups with direct-member links |
| M365 connection form appears above cloud object browsing | Data sources owns tenant/session health; object pages own everyday investigation |
| AD is both analyzer and escape route for unresolved user groups | AD analysis remains specialist; group/user object navigation stays in object workspaces |
| Device Cleanup is a top-level destination | Devices workflow, still directly linked by Action Center |
| SKU capacity sits inside M365 | Software & licenses owns tenant capacity; user profile owns assignment |
| Similar name often means same client in legacy views | Exact scoped identity confirms; weaker similarity remains candidate |
| Different pages retain different “last loaded” states | Each source/facet has read time, observation time, coverage and revision |
| Source unavailable can resemble absent/null | Source state explains whether absence is known, unqueried or inaccessible |
| “All clients” differs from global-search/comparison coverage | One declared working set and resolver, with visible source bounds |
| Main sidebar has 14 destinations | 13 destinations grouped by work, objects, operations and administration |

This is a migration of information ownership and navigation, not a visual
redesign or a replacement of specialist tools.

## I. Open decisions before implementation

These decisions do not block delivery of this analysis. Recommended defaults
are concrete proposals for the later start instruction.

| Decision | Recommendation | Alternative and trade-off | Needed by |
| --- | --- | --- | --- |
| Canonical object scope | Accounts and operational device/registration profiles; “Devices” label includes source-provided non-Windows objects | Person/physical-asset master needs HR/asset authority, history and more data | 0 |
| Cloud-only users | Allow Entra-anchored User profiles; AD stays authoritative for AD accounts; explicitly extend ADR 0019 | AD-only Users can stay, but leaves cloud accounts fragmented | 0 / 4 |
| Display navigation | C2's grouped sidebar, explicit Groups and Data sources; Cleanup under Devices | More aggressive grouping reduces labels but adds navigation layers and risks mega-pages | 0 / 7 |
| Historical Client module ownership | Extract concrete Clients read composition in phase 2; leave hygiene adapters and legacy tables in current owners initially | Keep everything in EmployeeLifecycle for speed, at ongoing naming/ownership cost | 2 |
| General AD groups | Approve bounded read-only group identity/list/direct-member projection | Reuse only privileged groups and DN text; cannot meet general group navigation goal | 5 |
| Stronger device evidence | First ship honest candidates; separately choose a bounded Windows registration/directory identity read | Make four-source confirmation a launch gate; requires collection seam and remote acceptance before first milestone | 0 / 8 |
| Manual persistent identity links | Defer; show candidates and retain explicit per-navigation selection only | Persistent overrides require author, reason, conflict handling, expiry, unlink and privacy/audit policy | Before any stored override |
| Complete large-environment inventory | Bounded in-memory working set with explicit completeness and targeted search | Complete persistent index/delta sync is more scalable for repeated global queries, but adds storage and governance scope | 6 |
| Source cache policy | Keep current per-source retention; derive profiles in memory | Persistent merged profiles conflict with current Graph session-only boundary | 2 |
| Contact-field precedence | AD preferred for AD account display; preserve all contradictory cloud values | Per-tenant configurable HR/sync-master policy is possible later; current WEC lacks that authority | 4 |
| Intune primary-user requirement | Keep associated user label in first consolidation; add explicit primary-user read only as a distinct approved follow-up | Include immediately, with endpoint/permission/role and coverage review | 4 / 8 |
| License reverse navigation | Filter already loaded assignment evidence with explicit partial coverage; otherwise show not available | New bounded Graph assignee query requires measured need and query validation | 6 |
| Source scope persistence | Retain non-secret directory/tenant identifiers and existing settings policy; source data/index stay ephemeral | Multi-tenant saved profiles need separate context and cache decisions | 1–2 |

### Security and privacy assessment of the proposed change

Consolidation makes already authorized personal/administrative data easier to
combine. That is useful, but increases the importance of scope isolation.

- No new Graph permissions are needed merely to display current selected
  fields in different sections. New group/identity/primary-user queries must
  have endpoint-specific permission and attribute review before implementation.
- Preserve delegated MSAL/WAM, tenant validation, GET-only Graph, fixed
  authority/host, paging/retry/timeout/cancellation, and sanitized errors.
  Do not merge Windows administrator and cloud authentication contexts.
- Never store tokens or credentials in object references, match evidence,
  snapshots, routes, query caches, telemetry or exports.
- No cloud profile/index persistence through generic frontend viewCache.
  Existing AD/Print/Nessus storage permissions are not permission to persist
  combined cloud evidence.
- Do not quietly add a cloud evidence export to an existing WEC/AD export.
  Existing export scope stays intact until a deliberate extension specifies
  columns, coverage, user action and retention implications.
- Do not make candidate matches drive remote execution, Cleanup decisions or
  Leaver completion. Operational actions retain their exact source target.
- A compromised desktop can still misuse authorized sessions. Consolidation
  neither expands permissions nor solves endpoint compromise.
- Keep Print unused-port deletion and opsi package maintenance in their
  established workflows. Object profiles gain navigation, not new write power.
- Preserve identifiers necessary for evidence; keep raw credentials and
  provider payloads out of logs. Technical disclosures expose only allowlisted
  troubleshooting data.
- No new person profiling, logon history, HR import, owner inference, directory
  write or permanent cloud synchronization is part of this proposal.

### Remaining technical debt and verification limits

The priority debt is identity loss through hostname keys, uneven provenance and
coverage, fragmented source/session lifecycles, and source-specific dead ends.
Other bounded cleanup candidates are the legacy Client composition owner,
Inventory N+1 reads and read-time removal of empty historical host rows,
duplicated host normalization and the oversized resource-union DTO if reused
outside its current source-browser role. None warrants a universal framework.

The previous M365 report records **815 backend tests, 475 frontend tests, a
warning-free Release build and 418 generated contracts**. Those numbers are
historical verification for the current integration, not a test run performed
by this analysis. No application change required a new test/build run here.
The analysis instead inspected production paths and existing test anchors.
Future phase gates above require new tests and full validation after code changes.

No live desktop, provider, tenant, large-directory or packaging acceptance was
performed for this report. The user-supplied successful M365 connection does not
by itself settle tenant-wide identity uniqueness, group coverage or registration
lifecycle behavior.

## Evidence index

All source references below point to the inspected local checkout. Code is the
basis for current-state statements; proposals are explicitly labelled.

| Ref | Evidence |
| --- | --- |
| S01 | [Route registry](C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/app/routeRegistry.tsx:69); [Host module registration](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Wec.Host/Program.cs:252) |
| S02 | [Client detail sections](C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/features/clients/ClientDetailPage.tsx:24); [User detail sections](C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/features/users/UserDetailPage.tsx:26); [Legacy redirect](C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/features/employeelifecycle/EmployeeLifecyclePage.tsx) |
| S03 | [Hygiene matching and source union](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.EmployeeLifecycle/Application/ItHygieneService.cs:288) |
| S04 | [Client list composition and key](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.EmployeeLifecycle/Application/ClientWorkspacePaging.cs:122); [ScanTarget key](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Wec.Core/Targets/ScanTarget.cs) |
| S05 | [Stored Client overview](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.EmployeeLifecycle/Application/ClientOverviewService.cs); [Overview refresh and user display](C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/features/clients/sections/OverviewSection.tsx:242) |
| S06 | [AD computer query/mapping](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.ActiveDirectory/Application/ComputerSearchService.cs); [Computer Core projection](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Wec.Core/Contracts/IAdComputerInventoryProvider.cs) |
| S07 | [AD user Core projection](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Wec.Core/Contracts/IDirectoryUserReadProvider.cs); [User mapper](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.ActiveDirectory/Application/DirectoryUserMapper.cs); [Directory read service](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.ActiveDirectory/Application/DirectoryUserReadService.cs) |
| S08 | [Graph DTO fields](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Wec.Core/Microsoft365/Microsoft365Contracts.cs); [Selected Graph queries](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Wec.Infrastructure/Microsoft365/GraphQueries.cs) |
| S09 | [Inventory snapshot](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.Inventory/Domain/HardwareSnapshot.cs); [Inventory acquisition](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.Inventory/Application/HardwareInfoService.cs) |
| S10 | [M365 correlation policy](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.Microsoft365/Application/Microsoft365CorrelationPolicy.cs); [Cloud context UI](C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/features/microsoft365/Microsoft365ContextPanel.tsx) |
| S11 | [M365 cache/context composition](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.Microsoft365/Application/Microsoft365Service.cs:121); [Cache defaults](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.Microsoft365/Microsoft365CacheOptions.cs) |
| S12 | [M365 resource navigation](C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/features/microsoft365/Microsoft365Page.tsx); [Cloud tables/details](C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/features/microsoft365/Microsoft365DataView.tsx) |
| S13 | [AD hygiene/member DTOs](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.ActiveDirectory/Domain/AdHygieneResult.cs); [AD identity disclosure](C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/features/activedirectory/directoryIdentity.tsx); [AD workspace](C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/features/activedirectory/ActiveDirectoryPage.tsx) |
| S14 | [User composition](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.UserManagement/Application/UserManagementService.cs); [User device links](C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/features/users/UserDevicesSection.tsx) |
| S15 | [Inventory SID lookup](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.Inventory/Application/InventoryUserDeviceRelationshipProvider.cs:23); [Client user projection](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.Inventory/Application/InventoryClientUserRelationshipProvider.cs) |
| S16 | [Inventory module behavior](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.Inventory/README.md); [Security checks/history](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.Security/README.md); [Health scope](C:/Users/vinzent.niederwieser/windows-enterprise-companion/docs/adr/0018-device-health-and-client-360.md) |
| S17 | [Patch current workflow](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.PatchManagement/README.md); [Print current workflow](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.PrintManagement/README.md); [Network scope](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.NetworkScan/README.md) |
| S18 | [Nessus parser and identity fallback](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.VulnerabilityManagement/Infrastructure/NessusApiClient.cs:320); [Nessus queries](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.VulnerabilityManagement/Application/VulnerabilityQueryService.cs); [Nessus data policy](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.VulnerabilityManagement/README.md) |
| S19 | [Global search data loading](C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/app/GlobalSearch.tsx); [Search result matching](C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/app/searchResults.ts); [Client/compare helper](C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/features/clients/clients.ts) |
| S20 | [Action Center contract](C:/Users/vinzent.niederwieser/windows-enterprise-companion/docs/adr/0020-computed-action-center-read-model.md); [Cleanup scope/export](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.DeviceCleanup/README.md) |
| S21 | [Reporting scope](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.Reporting/README.md); [Settings navigation](C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/features/verwaltung/SettingsSectionNavigation.tsx); [Source settings](C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/features/microsoft365/Microsoft365SettingsSection.tsx) |
| S22 | [Hygiene cache](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.EmployeeLifecycle/Application/ItHygieneSnapshotCache.cs); [Environment context](C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/shared/environment/EnvironmentContext.tsx) |
| S23 | [Inventory persistence](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Modules/Wec.Modules.Inventory/Persistence/EfHardwareSnapshotRepository.cs); [Frontend view cache](C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/shared/viewCache.ts); [Database composition](C:/Users/vinzent.niederwieser/windows-enterprise-companion/src/Wec.Infrastructure/Persistence/WecDbContext.cs) |
| S24 | [Semantic UI states](C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/shared/ui/SemanticStatusBadge.tsx); [Relationship model](C:/Users/vinzent.niederwieser/windows-enterprise-companion/frontend/src/shared/relationships/relationshipModel.ts); [UX audit closure](C:/Users/vinzent.niederwieser/windows-enterprise-companion/docs/frontend-ui-ux-roadmap.md:20) |
| S25 | [Core read pattern](C:/Users/vinzent.niederwieser/windows-enterprise-companion/docs/adr/0004-cross-module-read-contracts.md); [User authority](C:/Users/vinzent.niederwieser/windows-enterprise-companion/docs/adr/0019-ad-authoritative-user-management.md); [M365 boundaries](C:/Users/vinzent.niederwieser/windows-enterprise-companion/docs/adr/0021-microsoft-365-read-only-source.md) |
| S26 | [Prior M365 verification](C:/Users/vinzent.niederwieser/windows-enterprise-companion/docs/microsoft-365-implementation.md:279); [Roadmap decisions](C:/Users/vinzent.niederwieser/windows-enterprise-companion/ROADMAP_DECISIONS.md); [Execution register](C:/Users/vinzent.niederwieser/windows-enterprise-companion/ROADMAP_EXECUTION.md) |

Microsoft primary documentation checked on 2026-09-14:

- M1: [Graph device resource](https://learn.microsoft.com/en-us/graph/api/resources/device?view=graph-rest-1.0)
  — registration deviceId versus object ID; registered-owner and trust semantics.
- M2: [Graph Intune managedDevice resource](https://learn.microsoft.com/en-us/graph/api/resources/intune-devices-manageddevice?view=graph-rest-1.0)
  — azureADDeviceId, associated-user fields and explicit primary-users relationship.
- M3: [Graph user resource](https://learn.microsoft.com/en-us/graph/api/resources/user?view=graph-rest-1.0)
  — onPremisesSecurityIdentifier and synchronized identity properties.
- M4: [Entra Connect sourceAnchor design](https://learn.microsoft.com/en-us/entra/identity/hybrid/connect/plan-connect-design-concepts)
  — configured sourceAnchor, case/encoding and objectGUID/ConsistencyGuid distinction.

These references clarify semantics; they do not imply that every documented
Graph field or endpoint is implemented in WEC.
