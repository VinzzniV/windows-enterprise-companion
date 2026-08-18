# Wec.Modules.PatchManagement

Semi-automatic patch workflow hub on top of **opsi** (ADR 0008): dashboard,
inventory comparison, manufacturer version checks, controlled package
promotion, mandatory rollout preview and a persistent audit log.
The module talks to opsiconfd over JSON-RPC (`IOpsiClient` Core seam,
implemented in Infrastructure with `HttpClient` — no new dependencies).

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
| `patchmanagement/getDashboard` | `{ depotFilter? }` | `PatchDashboardResult` — depots, products with per-client states, inventory comparison, unmapped software |
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
composition (pure `Compose`), workflow-state derivation, preview targeting,
confirmation gate and audit writes with a substituted `IOpsiClient`.
The JSON-RPC client itself is tested in
`tests/Wec.Infrastructure.IntegrationTests/Opsi` against a fake HTTP
handler; persistence round-trips run real migrations in
`tests/Wec.Infrastructure.IntegrationTests/Persistence`.
