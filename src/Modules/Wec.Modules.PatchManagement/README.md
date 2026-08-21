# Wec.Modules.PatchManagement

Semi-automatic patch workflow hub on top of **opsi** (ADR 0008): dashboard,
inventory comparison, manufacturer version checks, controlled package
promotion, mandatory rollout preview and a persistent audit log.
The module talks to opsiconfd over JSON-RPC (`IOpsiClient` Core seam,
implemented in Infrastructure with `HttpClient` — no new dependencies).

## Frontend ownership

`usePatchManagementWorkspace` owns the opsi connection and dashboard request
lifecycle, credential-free cached-view migration/persistence, depot filter and
default resolution, live/stale state, refresh callbacks and the shared
product/all-source manufacturer-check request state. `PatchManagementPage`
owns only tab navigation plus package/client/drill selection and binds the hook
to feature workspaces. `PatchProductOverviewWorkspace` owns its
local package search/status filter, visible-product derivation, KPI and action
summary, product table and composition of `PatchProductDetailsPanel`. It
receives the parent-controlled selection, drill filter, shared check state and
narrow select/drill/close/check/refresh callbacks. The detail panel owns the
sticky Card/Close boundary, package/depot/manufacturer/inventory presentation
and composition of the controlled client, approval and deployment owners.
`PatchPackageBadge` keeps package-status presentation shared between overview
and detail.
`PatchAutomationWorkspace`
owns all three Automation-tab cards, mounts and retries `listVersionSources`
only while that tab is visible, and keeps the source form, save/delete
verification errors and source table within that boundary. It receives the
current product choices, connection/check state and narrow check/refresh
callbacks; confirmed checks and deletes still refresh the live dashboard.
The read-only `PatchClientFleetCard` owns the
global client/package-state workspace: its filter, server paging and sorting,
`listClientStates` request lifecycle, local retry and table presentation. It
receives only the live-connection flag, current dashboard snapshot and a
callback that opens the selected package (including the applicable client
state drill-down). `PatchProductClientsTable` is the corresponding read owner
inside `PatchProductDetailsPanel`: it owns the product-scoped
`listClientStates` lifecycle,
snapshot/product/filter reset, late-response protection, retry, server paging
and sorting, plus the complete table presentation. Its drill filter and client
selection remain controlled by `PatchManagementPage` because KPI drill-downs
set the former. `PatchPackageApprovalWorkflow` owns the product-scoped approval
status request, preferred test depot, package plan and explicit execution,
pilot approval, approved-only depot synchronization, local errors/outcome and
the complete approval presentation. Product or connection changes invalidate
that state and discard late responses. It composes `PatchDeploymentWorkflow`
as an independent child inside the approval chain. Deployment consumes the
controlled selection and owns `getRolloutPreview`/`requestRollout`, preview
loading and stale-response protection, confirmation, errors, outcome and the
preview table; product, depot, connection or selection changes invalidate only
that workflow. The
read-only `PatchAuditHistoryCard` owns `getAuditLog`, its entry/error state,
local retry, semantic result presentation and history table. It loads only
while the History tab is mounted; administrative workflows persist audit
records but neither prefetch nor refresh the hidden history view.
`PatchProductMappingsCard` owns the local-database mapping workflow: it loads
the current mapping list only while its exclusive tab is mounted and keeps
inputs, read retry, save/delete verification errors and both tables within that
boundary. The page supplies the current unmapped inventory rows and refreshes
its live dashboard only after the component confirms a mapping change.

Client-name shortening and the semantic workflow badge live with this fleet
table boundary and are reused by the package detail. The feature-local
`patchErrors` helper preserves the same typed opsi recovery guidance for page
and extracted request owners; it does not change Bridge errors or backend
contracts.

**Credential policy:** session-scoped in-memory (`OpsiSessionState`). Set by
a successful connect, cleared by disconnect, gone on process exit. Never
persisted, never logged; the bridge never returns the password.

**Write policy:** `productOnClient` rollout requests and repository-backed
package updates both require a preview and explicit confirmation. Package
updates run per depot through `IRemoteCommandExecutor` / Windows OpenSSH and
are verified through JSON-RPC afterward. Synchronization is locked until the
latest test-depot update is installed successfully on at least one pilot
client and explicitly approved (ADR 0015).

Products with an explicit `PackageAutomationProfiles` entry use the custom
build pipeline from ADR 0016. WEC resolves and hashes the manufacturer
artifact, uploads it with strict SCP, builds from an isolated workbench copy
on the test depot, and promotes that exact approved `.opsi` file after pilot
approval. Products without a profile keep the repository-backed updater path.

## Bridge actions

| Action | Payload | Result |
|---|---|---|
| `patchmanagement/connect` | `{ server, userName, password, trustServerCertificate }` | `OpsiConnectionStatusResult` — tests the connection (backend_info) and stores the session on success |
| `patchmanagement/disconnect` | `{}` | `OpsiConnectionStatusResult` — clears the session credentials |
| `patchmanagement/getConnectionStatus` | `{}` | `OpsiConnectionStatusResult` (never contains the password) |
| `patchmanagement/getDashboard` | `{ depotFilter? }` | `PatchDashboardOverview` — depots, client-free product metadata, inventory comparison and unmapped software; refreshes the process-local snapshot |
| `patchmanagement/listClientStates` | `{ depotFilter?, productId?, clientSearch?, productSearch?, state?, installationStatus?, page?, pageSize?, sortColumn?, sortDirection? }` | `PatchClientStatePage` — at most 100 client/package states from the matching successful snapshot |
| `patchmanagement/getRolloutPreview` | `{ productId, depotFilter?, clientIds? }` | `RolloutPreview` — affected clients; no `clientIds` = outdated + failed clients |
| `patchmanagement/requestRollout` | `{ productId, clientIds, depotFilter?, confirmed }` | `RolloutRequestOutcome` — refused without `confirmed: true`; always audited |
| `patchmanagement/preparePackages` | `{ productId, stage, depotIds }` | Audited SSH command preview for `TEST` or `DEPOT_SYNC` |
| `patchmanagement/executePackageUpdate` | `{ productId, stage, depotIds, confirmed }` | Executes and verifies each depot; returns per-target results |
| `patchmanagement/approvePackagePilot` | `{ productId, confirmed }` | Approves only when a pilot client on the test depot reports the tested version |
| `patchmanagement/getPackageWorkflowStatus` | `{ productId }` | Latest test, approval, synchronization and error state |
| `patchmanagement/getAuditLog` | `{ limit? }` | `AuditLogResult` — newest first |
| `patchmanagement/listMappings` / `saveMapping` / `deleteMapping` | mapping fields | `MappingsResult` — manual inventory-name ↔ opsi-productId mapping (audited) |
| `patchmanagement/listVersionSources` / `saveVersionSource` / `deleteVersionSource` | product id, HTTPS URL, regex | Persisted manufacturer version sources |
| `patchmanagement/checkVendorVersions` | `{ productIds? }` | Checks selected or all configured sources and audits old/new version or failure |

`server` accepts what an admin types (`opsi.example.local`,
`host:4448`, full https URL); missing scheme becomes `https`, missing port
becomes `DefaultServicePort`. `trustServerCertificate` disables chain
validation for that session only — opsi's default CA is self-signed.

## Workflow states

Full lifecycle in `PatchWorkflowState` (ADR 0008): `Detected`,
`UpdateAvailable`, `DownloadNeeded`, `PackagePrepared`, `Uploaded`,
`ReadyForPilot`, `Approved`, `RolloutRequested`, `Completed`, `Failed`.
The MVP derives Detected / UpdateAvailable / RolloutRequested / Completed /
Failed from `productOnClient` data; the remaining states are reserved for
the package-preparation pipeline.

The frontend presents this client workflow separately from aggregate package
health. `Completed` is the current lifecycle, `UpdateAvailable` remains an
available update and `Failed` is failed execution. `Detected` and every
incomplete preparation or rollout milestone use the pending lifecycle with the
exact milestone retained as adjacent context. Unknown states are shown as
unavailable workflow status with their raw value confined to technical detail.
This mapping is shared by client tables, rollout preview and the status filter;
it does not change derivation or bridge values.

The dashboard's aggregate `PatchPackageStatus` remains a separate backend
contract. The frontend presents `CURRENT` / `UPDATE_AVAILABLE` as lifecycle,
`DEPOT_DEVIATION` as a health warning, `MISSING_ON_DEPOT` as missing
availability, `CHECK_FAILED` as failed execution and `DEPLOYMENT_PENDING` as a
pending lifecycle. Adjacent context preserves the concrete package meaning;
unknown values are shown as unavailable status with the raw value retained only
as technical detail. This presentation mapping does not change status
derivation, filters or bridge values.

Manufacturer-source presentation is a third, independent status family.
Enabled sources map `SUCCESS` / `FAILED` to succeeded / failed execution and
`NOT_CHECKED` to unknown availability with explicit `Not checked` context;
packages without a source use not-configured availability. Persisted disabled
sources use the disabled lifecycle and are not counted as active by the
scheduled-check summary. Unknown check values are neutral unknown states with
the raw value confined to technical detail. Package overview, product detail,
scheduled checks and the source table share this mapping; persisted values,
automatic due checks and bridge contracts remain unchanged.

The Package Approval Chain is a fourth, separately presented family. Its
read-only request is `Running` while loading and `Failed` if loading fails;
without a verified live result it is `Unknown`. A successful test-depot update
and pilot approval are `Succeeded` with their concrete milestone shown beside
the badge, while a successfully loaded workflow without a test update is
`Pending · Test update`. Frontend loader state is keyed to the selected product
and late responses are discarded, so an approval from one product cannot be
shown or unlock depot synchronization for another. `PackageWorkflowStatus`,
audit derivation, approval rules and bridge values are unchanged.

The routed Patch header presents the read-only opsi connection check
independently from all package state. A pending check is `Running`, a failed
check is `Failed`, a confirmed session is `Available`, and a successfully
verified response without a session is `Unknown · Not connected`. Server URL,
account and opsi version remain visible beside the canonical status. The UI
does not claim `Not connected` before the request resolves; session restoration,
Credential Manager access and bridge values remain unchanged.

## Data sources & mapping

- opsi: depots (= locations), clients with depot assignment
  (`clientconfig.depot.id`, fallback = configserver), localboot products,
  `productOnDepot` versions, `productOnClient` states.
- WEC inventory: installed software per stored host via the
  `IInstalledSoftwareInventoryProvider` Core contract (ADR 0004).
- Join: manual mapping table `patchmanagement_product_mappings` —
  suggestions only on exact name matches, no fuzzy auto-matching.
- Manufacturer versions: one optional HTTPS URL and bounded regular expression
  per product (`patchmanagement_version_sources`, ADR 0014). Checks older than
  the configured interval are refreshed when the connected dashboard opens.

## Audit

`patchmanagement_audit_entries`: timestamp, Windows user, action, product,
target clients/depot, old/new version, serialized preview/output, result
(`SUCCESS`/`FAILED`/`PLANNED`), error. Package execution writes one entry per
target depot so partial failures remain visible.

The frontend presents audit results independently from package, client,
manufacturer and approval status. `SUCCESS` / `FAILED` are succeeded / failed
execution. `PLANNED` means that the read-only package preview was created
successfully and is shown as `Succeeded · Preview created`; it does not imply a
queued deployment. Unknown values fall back to unavailable status and retain
their raw value only as technical detail. Persisted result values, action names,
history ordering and audit writes remain unchanged.

## Options (`Wec:PatchManagement`)

| Option | Default | Purpose |
|---|---|---|
| `DefaultDepotFilter` | empty (all depots) | Optional depot the UI preselects (matched against id + description) |
| `OpsiRequestTimeout` | 30 s | Per-request timeout against opsiconfd |
| `DefaultServicePort` | 4447 | Port used when the server input names none |
| `AuditHistoryLimit` | 100 | Default page size of the audit history |
| `ManufacturerCheckInterval` | 1 day | Age after which opening the dashboard refreshes a configured source |
| `ManufacturerRequestTimeout` | 20 s | Per-source timeout for manufacturer version checks |
| `SshUserName` | `root` | SSH account; authentication comes from agent/key, never a password |
| `SshIdentityFile` | empty | Optional private-key path; empty uses OpenSSH defaults/agent |
| `SshConnectTimeout` | 10 s | SSH connection timeout |
| `PackageCommandTimeout` | 30 min | Overall timeout per depot package command |
| `PackageTransferTimeout` | 15 min | Overall timeout for manufacturer download and SCP transfer |
| `PackageAutomationProfiles` | Greenshot profile | Opt-in release URL, version-bound artifact pattern (`{version}`), workbench and stable installer path per supported custom package |
| `UseNonInteractiveSudo` | `false` | Prefix updater command with `sudo -n` |

## Tests

`tests/Wec.Modules.PatchManagement.Tests` — URL normalization, dashboard
composition (pure `Compose`), snapshot identity and paging/filtering,
workflow-state derivation, preview targeting, confirmation gate and audit
writes with a substituted `IOpsiClient`.
The JSON-RPC client itself is tested in
`tests/Wec.Infrastructure.IntegrationTests/Opsi` against a fake HTTP
handler; persistence round-trips run real migrations in
`tests/Wec.Infrastructure.IntegrationTests/Persistence`.
