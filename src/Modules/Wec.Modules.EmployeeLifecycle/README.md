# IT Lifecycle / Environment Health MVP

The existing `Wec.Modules.EmployeeLifecycle` project and bridge module name are
retained to keep host composition, contracts and existing installations
compatible. Its read-only device hygiene data is presented as **Fleet posture**
inside the canonical Clients workspace and correlates Active Directory with
Kaspersky Security Center (KSC), opsi and Nessus.

## Data flow

```text
Active Directory -- IAdComputerInventoryProvider ---\
KSC OpenAPI ------ KasperskySecurityCenterClient ----+
opsi ------------- IOpsiComputerInventoryProvider ---+-> HygieneSourceLoader -> ItHygieneService
Nessus ----------- INessusComputerInventoryProvider -/                           |- request-bound snapshot -> UI
Inventory history - IInventoryClientSnapshotProvider --\                     |- filtered/sorted device page -> UI
Saved targets ----- ISavedClientTargetProvider ---------+-> client merge -----`- filtered/sorted client page -> UI
```

`HygieneSourceLoader` owns provider I/O, request-scoped credential resolution
and source-state mapping. `ItHygieneService` correlates those results into the
request-bound snapshot. `HygieneAssessmentPolicy` is the pure, characterized
boundary for finding severity and source-coverage semantics reused by later
read models.

- AD reuses the existing LDAP reader, domain discovery, credentials and paging.
  `lastLogonTimestamp` is exposed as `LastLogonDate`; like every replicated AD
  last-logon value it can lag behind the exact logon time.
- KSC uses its HTTPS OpenAPI (default TCP 13299), authenticates with KSCBasic,
  calls `HostGroup.FindHosts`, reads result chunks and releases the temporary
  accessor. No host, group, product or task mutation is called.
- Computer names are trimmed, upper-cased and reduced from FQDN to short name.
  This is intentionally the only identity correlation in phase 1.

## Assessment

The service emits only these findings:

| Finding | Condition |
|---|---|
| `MissingKaspersky` | enabled AD computer, no normalized KSC match |
| `OrphanKaspersky` | KSC computer, no normalized AD match |
| `MissingKasperskyAgent` | KSC computer has no reported Network Agent version |
| `MissingKes` | KSC computer has no reported KES version |
| `StaleAd` | AD LastLogonDate exceeds warning/critical threshold |
| `StaleKaspersky` | KSC LastSeen exceeds warning/critical threshold |
| `OutdatedAgent` | numeric Agent version is below configured target |
| `OutdatedKes` | numeric KES version is below configured target |

Unknown timestamps and unconfigured/unknown target versions do not create a
finding. A critical stale finding results in `CleanupCandidate`; this is a
display status only and never triggers cleanup.

## Bridge action

The existing bridge module name remains `employeelifecycle`.

| Action | Payload | Result |
|---|---|---|
| `getHygiene` | optional AD and KSC connection/credential overrides plus optional `operationId` | correlated `ItHygieneResult` |
| `getHygieneOverview` | optional AD/KSC overrides, optional `operationId` and explicit `force` refresh | sources, summary, assessment time and known hosts without device rows |
| `listHygieneDevices` | optional AD/KSC overrides and `operationId` plus search, filter, page, page size and allowlisted sort | at most 100 correlated device rows plus filtered total |
| `listClientWorkspace` | optional AD/KSC overrides and `operationId` plus client search, posture/source filter, grouping, page, page size, allowlisted sort and explicit `force` refresh | at most 100 de-duplicated rows without grouping; grouping pages complete OS/site groups, paged by at most 100 groups, with exact filtered, snapshot and group totals, source/time/domain metadata and a summary recalculated over those canonical de-duplicated hygiene rows |

Client 360 uses a separate presentation-level bridge action while this project
remains the host for the existing Clients workspace:

| Module/action | Payload | Result |
|---|---|---|
| `clients/getOverview` | required client host | latest persisted Inventory, installed-software, Health and Security summaries with per-source freshness, completeness, coverage and detail-tab links |

`clients/getOverview` reads only existing projections. Opening Client 360 never
starts Inventory, Health, Security or external management-provider work. AD,
Kaspersky, opsi and Nessus posture remains request-bound and is loaded only by
an explicit user action.

Cold loads with an `operationId` publish the typed event
`employeelifecycle/hygieneProgress`. It reports the current load phase, elapsed
start time, completed source count, per-source state and item count, and a
partial correlated device count/summary after each AD, Kaspersky, opsi or Nessus
source completes. The UI uses these events for progress presentation only. A
generic bridge cancel envelope cancels the correlated request and propagates the
request token into all four parallel providers.

The frontend presents these source states through the shared semantic status
contract in load progress, Fleet posture and the client Overview. `Available`
is availability, `Running` is execution, partial or truncated results are
partial execution, a source that is not connected is not configured, and an
unavailable source is failed execution. The concrete `Not connected`, `Source
unavailable` and `Result truncated` distinctions remain adjacent context; this
presentation mapping does not alter the source or progress bridge values.

Client presentation keeps assessment separate from source status. `Healthy`
and `Warning` are health, while both a critical assessment and a cleanup
candidate use Health `Critical`; the latter retains `Cleanup candidate` as
context. An incomplete assessment is Execution `Partial`, and an unmanaged
client is Availability `Unknown`. Source presence is `Available`, `Missing` or
`Not applicable`; a present source is `Fresh`, `Stale` or `Unknown` when no
timestamp is known. Disabled AD objects and outdated Kaspersky components are
Lifecycle `Disabled` and `Update available`. The Compare picker independently
labels stored Inventory and Security snapshots as `Available` or `Missing`.
These are frontend presentation mappings only: findings, stale thresholds,
assessment enums, timestamps and bridge payloads remain unchanged.

The Clients master also exposes a manual read-only connectivity check for the
currently visible page. Its existing host-bridge response contains separate
ICMP `reachable` and TCP/5985 `manageable` evidence. The frontend shows the
request as Execution `Running`, a transport failure as Execution `Failed`, at
least one responding channel as Availability `Available`, and no response (or
an omitted host result) as Availability `Unknown`; it never derives a
definitive `Offline` claim from a missing ping reply. Concrete channel evidence
and the local check time remain visible per row. This does not alter source
collection, hygiene evaluation, paging, probe limits, timeouts or the bridge
contract.

The host retains only the latest successful assessment for the matching
connection request. The request is identified by a SHA-256 fingerprint; raw
credentials and the presentation-only operation ID are not retained as cache-key
text. Paging therefore does not
repeat the external AD/KSC/opsi/Nessus reads. An explicit overview refresh
loads the sources once and atomically replaces the snapshot after success.
The cache is process-local, not persisted, and failures or cancelled loads never
replace the latest successful snapshot.
Client-workspace pages additionally read narrow host/timestamp and client-target
projections through the cross-module contracts from ADR 0004. They do not
reference Inventory or Targets projects, repositories or persistence entities.

Fleet-posture summary metrics in Clients are direct drill-downs into the shared
server-side hygiene filters. The active filter is stored as
`#/clients?posture=<FILTER>` and is shared with the filter select, so a problem
view can be linked, restored and navigated with browser history. KPI buttons
expose the active state through `aria-pressed`; `Assessed devices` removes the
parameter and returns to `ALL`. The legacy
`#/employeelifecycle?filter=<FILTER>` route remains a small allowlist-based
redirect to the canonical URL; it no longer renders a second device table.
The posture selector also provides focused stale filters for AD, Kaspersky and
opsi, plus `Missing in Active Directory` and `Disabled in Active Directory`.
This makes it possible to review old inventory records and run the existing
read-only connectivity check against the visible AD-missing page.

When a user opens a client from the Clients table and returns through `All
clients`, the current page, filters, sort, scroll position and manual
connectivity evidence are kept in browser history state. The evidence is not
persisted and is replaced by a new connectivity check.

Credentials are request-scoped and held in memory only. KSC has a separate
session sign-in under Settings so it does not replace the global Windows/AD
administrator. The frontend uses the newest saved domain controller. KSC server,
thresholds, target versions and an optional private-certificate thumbprint are
edited under **Settings → IT Lifecycle / Environment Health**. Saving merges
the values into `%APPDATA%\Wec\usersettings.json`; they apply after restart.
Ignored KSC administration groups are comma-separated provider entity names:
the Settings example hint uses the English product language, but configured
group names are loaded and saved verbatim. Passwords are never part of the
saved settings.

## Options (`Wec:ItLifecycle`)

| Option | Default | Purpose |
|---|---:|---|
| `InventoryLimit` | 10000 | maximum devices read from each source |
| `StaleWarningDays` | 60 | warning threshold |
| `StaleCriticalDays` | 90 | cleanup-candidate threshold |
| `TargetAgentVersion` | empty | desired Network Agent version; empty disables the rule |
| `TargetKesVersion` | empty | desired KES version; empty disables the rule |
| `Kaspersky:Server` | empty | KSC Administration Server host name |
| `Kaspersky:Port` | 13299 | KSC OpenAPI TLS port |
| `Kaspersky:RequestTimeout` | 60 seconds | per-request timeout |
| `Kaspersky:TrustedCertificateThumbprint` | empty | exact SHA-1/SHA-256 thumbprint accepted in addition to platform trust |
| `Kaspersky:ExcludedAdministrationGroups` | `Nicht für Kaspersky geeignete Geräte` | provider group names excluded from inventory and orphan checks; values are not translated |

## Legacy data

The former employee/case/task handlers are no longer registered and their UI
screen is removed. Existing SQLite tables and source types are intentionally
not dropped by this MVP, so installing the read-only replacement cannot destroy
previous lifecycle data. The legacy route is redirect-only. The hygiene feature
adds no database tables.

ADR 0019 assigns future User Management to an AD-authoritative read-only module.
The five legacy tables are frozen: WEC does not write or automatically import
them, and deleting them requires a separately approved destructive migration.

## Tests

- AD inventory mapping and LDAP contract tests
- KSC OpenAPI login, chunk parsing, limit and authentication-error tests
- name correlation, independent stale rules and version comparison tests
- request-bound snapshot reuse and force-refresh tests
- correlated source-progress, partial-summary and cancellation tests
- server-side device search/filter/sort/page tests
- server-side three-source client merge, search/filter/group/sort/page tests
- frontend Fleet-posture KPI deep-links, summary, canonical Clients paging/search/filter and legacy-route tests
