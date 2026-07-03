# Wec.Modules.PatchManagement

Semi-automatic patch workflow hub on top of **opsi** (ADR 0008): dashboard,
inventory comparison, mandatory rollout preview and a persistent audit log.
The module talks to opsiconfd over JSON-RPC (`IOpsiClient` Core seam,
implemented in Infrastructure with `HttpClient` — no new dependencies).

**Credential policy:** session-scoped in-memory (`OpsiSessionState`). Set by
a successful connect, cleared by disconnect, gone on process exit. Never
persisted, never logged; the bridge never returns the password.

**Write policy:** the only write is `productOnClient` action requests
(`actionRequest = "setup"`), and it requires a preceding preview plus an
explicit confirmation flag. Everything else is read-only.
`opsi-package-updater` cannot run over JSON-RPC — "Prepare packages"
records the exact server command as a **planned** action instead of
pretending to execute it (SSH execution is a documented follow-up).

## Bridge actions

| Action | Payload | Result |
|---|---|---|
| `patchmanagement/connect` | `{ server, userName, password, trustServerCertificate }` | `OpsiConnectionStatusResult` — tests the connection (backend_info) and stores the session on success |
| `patchmanagement/disconnect` | `{}` | `OpsiConnectionStatusResult` — clears the session credentials |
| `patchmanagement/getConnectionStatus` | `{}` | `OpsiConnectionStatusResult` (never contains the password) |
| `patchmanagement/getDashboard` | `{ depotFilter? }` | `PatchDashboardResult` — depots, products with per-client states, inventory comparison, unmapped software |
| `patchmanagement/getRolloutPreview` | `{ productId, depotFilter?, clientIds? }` | `RolloutPreview` — affected clients; no `clientIds` = outdated + failed clients |
| `patchmanagement/requestRollout` | `{ productId, clientIds, depotFilter?, confirmed }` | `RolloutRequestOutcome` — refused without `confirmed: true`; always audited |
| `patchmanagement/preparePackages` | `{ productIds }` | `PreparePackagesPlan` — the opsi-package-updater command as a planned, audited action |
| `patchmanagement/getAuditLog` | `{ limit? }` | `AuditLogResult` — newest first |
| `patchmanagement/listMappings` / `saveMapping` / `deleteMapping` | mapping fields | `MappingsResult` — manual inventory-name ↔ opsi-productId mapping (audited) |

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

## Audit

`patchmanagement_audit_entries`: timestamp, Windows user, action, product,
target clients, serialized preview, result (`SUCCESS`/`FAILED`/`PLANNED`),
error. Every action writes one — including failed and planned actions.

## Options (`Wec:PatchManagement`)

| Option | Default | Purpose |
|---|---|---|
| `DefaultDepotFilter` | Denkingen | Depot the UI preselects (matched against id + description) |
| `OpsiRequestTimeout` | 30 s | Per-request timeout against opsiconfd |
| `DefaultServicePort` | 4447 | Port used when the server input names none |
| `AuditHistoryLimit` | 100 | Default page size of the audit history |

## Tests

`tests/Wec.Modules.PatchManagement.Tests` — URL normalization, dashboard
composition (pure `Compose`), workflow-state derivation, preview targeting,
confirmation gate and audit writes with a substituted `IOpsiClient`.
The JSON-RPC client itself is tested in
`tests/Wec.Infrastructure.IntegrationTests/Opsi` against a fake HTTP
handler; persistence round-trips run real migrations in
`tests/Wec.Infrastructure.IntegrationTests/Persistence`.
