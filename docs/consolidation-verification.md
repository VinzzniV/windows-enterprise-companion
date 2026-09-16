# Object-centered consolidation — verification and preservation

Status: navigation preparation; final completion is not yet claimed.
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

- Release build on 2026-09-16: no warnings or errors; 494 generated contract
  types. The additive request fields preserve existing caller defaults.
- Full backend regression after source/navigation preparation: 1,022 tests pass.
- Full frontend regression: 555 tests in 97 files pass with two workers. The
  first unbounded run hit five load-related timeouts and a group test that
  asserted before the restored cached page rendered. The assertion now waits
  for the page; the test runner bounds workers without relaxing assertions or
  timeouts. Production build passes.

## Remaining completion evidence

The C2 sidebar switch, final full regression/build/audit/architecture checks,
desktop normal/narrow/keyboard smoke, documentation reconciliation and verified
Git milestone remain open. Live company AD/KSC/opsi/Nessus, Graph/Intune tenant
acceptance and a designated remote client have not been tested in this run.
They remain explicit release-acceptance gates under D-003/D-008, not claimed
passes. No tag, installer publication or GitHub Release is authorized.
