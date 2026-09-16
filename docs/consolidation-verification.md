# Object-centered consolidation — verification and preservation

Status: phases 0–7 and 9 implemented and locally verified on 2026-09-16.
GitHub build/packaging evidence is tracked on the associated Draft PR.
Authority: ADR 0022 and `consolidation-analysis-and-plan.md`, especially G2.

## Preservation evidence before the sidebar switch

The specialist implementations remain in their existing feature modules. New
entry points compose them or resolve an address/identity before navigation.
No export schema is expanded with cloud evidence, and no write action is added.

| G2 route or workflow | Retained destination and evidence |
| --- | --- |
| `/`, `/actions` | Existing Dashboard and computed Action Center; original coverage, filters and severity remain. Action Center tests additionally check individual Cleanup evidence keys and exact Nessus keys. |
| `/cleanup?host=...` | Existing Cleanup assessment and export workflows. Duplicate host links require an individual evidence row; unresolved rows cannot probe or export a Ping target. DeviceCleanup service, workbook and UI suites cover this boundary. |
| `/clients?posture=...`, legacy Lifecycle filters | Existing posture, source/group/sort/page controls, saved targets and explicit cancellable batch scans remain linked from Devices. ClientsPage, bulk action and legacy route suites remain authoritative. |
| `/clients/compare` | Existing searchable comparison picker, recent targets and Inventory/Security comparison remain linked from Devices. Compare tests remain unchanged. |
| `/clients/:host` | The compatibility entry preserves the entire decoded address and resolves the WEC workspace. Device composition offers an exact target or separate alias candidates. It never selects the first namesake. ClientEntryPage and Clients composition tests cover these paths. |
| Client Inventory, Diagnostics/Health, Security, Events, Printers, Reporting | Original sections remain usable on the resolved exact host, with the original credentials and local/remote selection. Section aliases survive resolution; the operational return link leads to the source profile. ClientDetailPage and specialist suites cover explicit scans, export readiness, errors, cancellation and history. |
| Save/unsave and PowerShell | Retained in the exact Windows target view. Multiple saved records have individual removal buttons. PowerShell uses the original endpoint and session credentials. |
| Client `section=microsoft365` | Scoped device identity and cloud relationships expose independent Entra/Intune/registered-owner evidence and candidates. The original source context panel remains available in the exact Windows view. |
| `/users/:objectId?section=...` | The existing AD read supplies the verified directory scope; the alias retains the selected section and directory endpoint when opening the scoped account. Identity, access, devices, Leaver and Microsoft 365 aliases remain covered by UserDetailPage/ScopedUserProfilePage tests. |
| M365 Tenant / authentication | Data sources reuses the existing WAM connection, feature flags, permissions, disconnect/cache clearing and tenant status UI. Settings remains the saved configuration editor. |
| M365 Users/User, Devices/Device, ManagedDevices | Source-filtered object lists and scoped Entra/Intune profiles; resource adapters preserve tenant/native IDs and never infer a tenant from a name. Intune keeps every enrollment and the associated-user label. |
| M365 Groups/Group/GroupMembers | Source-native group profiles and typed direct-member links. AD member pages and filters survive URL history; history reads are cache-only. Limited/unknown member fields remain visible. |
| M365 Licenses / UserLicenses | Software & licenses reuses SKU/service-plan/capacity rendering; account licenses remain in the account profile. SKU reverse navigation filters only loaded assignments in the specified tenant. Missing/expired catalogue data never triggers a source fetch on opening. |
| UserGroups/UserDevices/DeviceOwners | Focused scoped account/device destinations retain direct-group, registered-device and registered-owner semantics. They never imply effective permissions or ownership. |
| UserActivity/UserRegistration | The Activity section retains separate optional report reads and errors. MFA registration remains distinct from enforcement. |
| `/activedirectory` | Existing domain/DC/count/hygiene analysis, rule pages, four privileged-group browsers, connection tests and CSV export remain in Data sources and at the old route. |
| `/vulnerabilities?tab=...&asset=...` | Assets, findings, instance details, scans and trend remain. Exact source keys select their own records; legacy full-address filters preserve all address candidates and individual instance keys. |
| `/patchmanagement` | Existing opsi/Winget package maintenance and audit history remain. Software & licenses exposes Packages, Client versions, Licenses, Winget packages and History without changing write gates or adding rollout. |
| `/printmanagement`, `/networkscan`, `/reporting` | Existing queue/device/SNMP/DHCP/CCRX/history/diff/export/confirmed-port-delete, explicit scan/cancellation and single-subject report workflows remain independently routed. |
| `/settings?section=...`, `/logs` | Existing section anchors, validation, dirty/restart states, secure credential handling, log filters/details and hide-previous semantics remain. Data sources adds session controls, not a second settings editor. |

## Current automated evidence

- The sidebar now exposes the C2 Work, Objects, Operations and Administration
  groups. All 81 affected navigation/object/source tests and the production
  build pass after the switch. Hidden specialist routes keep their canonical
  parent highlighted; Cleanup and comparison remain globally searchable.

- Release build on 2026-09-16: no warnings or errors; 494 generated contract
  types. The additive request fields preserve existing caller defaults.
- Full backend regression after closure fixes: 1,024 tests pass.
- Full frontend regression: 559 tests in 97 files pass with two workers. The
  first unbounded run hit five load-related timeouts and a group test that
  asserted before the restored cached page rendered. The assertion now waits
  for the page; the test runner bounds workers without relaxing assertions or
  timeouts. Production build passes.
- A locked `npm ci`, production build and High-severity NPM audit pass. Two
  pre-existing Moderate findings affect Vitest/@vitest/mocker development tools
  (GHSA-82fw-gwwq-j7x9); the proposed fix is a separate major test-tool upgrade.
- Project-reference/package checks pass for all 20 production projects: Core
  keeps only DI abstractions, modules and Infrastructure reference only Core,
  and Host remains the composition root. Profile consumers contain no direct
  Graph/LDAP/SQL clients. New cloud/index/profile UI paths contain no browser
  persistence or direct network reads. Bridge-origin, fixed Graph query/scope,
  credential/session, cancellation, expiry and no-scan boundaries have automated
  coverage. No directory write, export expansion or destructive migration was added.
- A 10,000-device fixture retained exactly the configured 5,000 observations,
  exposed partial coverage and the original source total, and filtered the last
  retained native ID before paging. Local capture was 61 ms; filter/page 9 ms.
  This is an observed bounded-data smoke, not a cross-machine latency guarantee.

## Desktop evidence and acceptance journeys

The Release executable ran with an isolated database/log/WebView profile.
WebView2 and real migrations initialized successfully; no WEC-owned TCP listener
or Error/Fatal log entry was observed. At a 1280×800 client size the four sidebar
groups, empty Devices workspace, independent filters and contextual tools were
usable. Ctrl+K, Tab/Shift+Tab, typing and arrow selection worked in global search.
The user stopped Computer Use with physical Escape before narrow-window testing
completed. Narrow-window desktop acceptance is therefore **unverified**, despite
automated responsive-navigation coverage. No further UI input was issued.

The existing Dashboard used its configured bounded read-only opsi connection
and returned status/product data. This is limited source evidence, not complete
opsi acceptance. No AD/Graph discovery, new remote probe, write or export was
executed as part of this desktop check. A pre-existing EF multiple-collection
query warning appeared on the Dashboard Security read; it is documented as debt.

| C7 journey | Automated evidence |
| --- | --- |
| User → device → account section | ScopedUserProfile, DeviceProfile, typed relationship and working-set history suites preserve source IDs, endpoint and section. |
| Observed Windows SID → account | Exact SID resolver and scoped account tests preserve ambiguity and reject display-name resolution. |
| Group → direct user/device/group | GroupManagement, directory range/page and group UI suites cover typed members, limited objects, history and no recursive expansion. |
| Entra/Intune-only device | Device composition/UI tests retain unknown fields, all enrollments and source errors without selecting a local Windows target. |
| Account → SKU → loaded assigned users | Scoped account, license workspace and working-set tests preserve tenant/SKU, cached-only opening, expiry and partial assignment coverage. |
| Finding → source/device → return | Action Center mapping, exact Nessus key, compatibility route and list history tests preserve individual subjects and query parameters. |

## Remaining acceptance limits and technical debt

Phase 8 identity collection is excluded. Name/address/UPN correlations remain
candidates; UUID-less Nessus report locators do not imply stable physical assets.
General group lists do not fetch member totals or evaluate privileges per row;
direct-member and existing privileged-allowlist views state their own coverage.
Lists cannot prove company-wide absence. Legacy wrappers and frozen tables remain
to preserve specialist behavior and bookmarks. The Moderate test-tool advisories,
existing EF Dashboard query warning and unsigned packaging remain separate debt.

Live company AD/KSC/opsi/Nessus, Graph/Intune tenant
acceptance and a designated remote client have not been tested in this run.
They remain explicit release-acceptance gates under D-003/D-008, not claimed
passes. No tag, installer publication or GitHub Release is authorized.
