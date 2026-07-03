# ADR 0008: opsi Patch Management Workflow Hub

- **Status:** Accepted
- **Date:** 2026-07-03
- **Deciders:** Vinz
- **Supersedes:** — (first deliberate exception to the read-only rule;
  extends ADR 0007 credential policy with a session scope)

## Context

Patch management in this environment runs over **opsi** (uib): admins use the
opsi GUI, SSH onto the Linux opsi server, and `opsi-package-updater`. Several
depots/locations exist; the primary location is **Denkingen**. The pain point
is vulnerability patching: finding out *which* installed software is outdated,
*which* clients are affected at *which* location, and driving the
prepare → pilot → approve → rollout sequence without blind automation.

WEC gets a **Patch Management** module as a semi-automatic workflow hub:
dashboard, comparison against WEC's own inventory, preview of affected
clients, and an audit trail. Rollout never happens without an explicit admin
confirmation.

Constraints carried over from the existing architecture:

- Modules reference only `Wec.Core`; transports hide behind Core seams.
- Expected failures are typed `Result` errors, never generic strings.
- No password is ever persisted or logged.
- No hardcoded tunables; configuration is options.

### Options considered for the opsi transport

| Option | Assessment |
|---|---|
| **opsi JSON-RPC web service (opsiconfd, HTTPS :4447)** | The official admin API — the same one opsi-configed uses. Products, depots, clients, per-client product states and action requests are all first-class JSON objects. `HttpClient` + Basic auth, zero new dependencies. |
| SSH + CLI parsing (`opsi-cli`, `opsi-package-updater`) | Needs an SSH client dependency (SSH.NET) and screen-scraping CLI output that changes between opsi versions. Fragile as the *primary* data channel. Still the only channel for running `opsi-package-updater` — see below. |
| opsi REST endpoints (opsiconfd 4.3) | Newer, less complete than JSON-RPC, version-coupled. JSON-RPC works on 4.2 and 4.3. |

### Options considered for credentials

| Option | Assessment |
|---|---|
| **Session-scoped in-memory** | The admin performs many calls against the same server in one sitting; re-entering the password per request (ADR 0007 model) would be hostile. Credentials live in a module-owned singleton, cleared on disconnect and gone on process exit. Never persisted, never logged. |
| Per-request only (ADR 0007) | Right for occasional WMI scans, wrong for a workflow hub with dozens of API calls per session. |
| Windows Credential Manager | Explicitly excluded by requirement for this feature. Revisit only if a "save connection" feature is requested (would be its own ADR revision). |

## Decision

1. **Transport is the opsi JSON-RPC API** behind a new Core seam
   `IOpsiClient` (implementation `JsonRpcOpsiClient` in Infrastructure,
   `HttpClient`-based, Basic auth). Read operations: server/auth check,
   depots, clients (with depot assignment), products with versions per depot
   (`productOnDepot`), per-client product states (`productOnClient`).
2. **Credential policy: session-scoped in-memory.** `OpsiSessionState`
   (module-owned singleton) holds server URL, user name and password after a
   successful "Test connection". Cleared by disconnect or app exit. The seam
   itself stays stateless — every `IOpsiClient` call takes an
   `OpsiConnection` value. Passwords never appear in logs, errors, audit
   entries or the database.
3. **Certificate handling:** opsi servers use the self-signed opsi CA by
   default. The connection form has an explicit, session-scoped
   "Trust server certificate" opt-in (default off) that disables chain
   validation for that connection only. Without it, an untrusted certificate
   surfaces as a distinct, explained error.
4. **First write capability, strictly gated.** Requesting a rollout sets
   `actionRequest = "setup"` on `productOnClient` objects via JSON-RPC — the
   same operation an admin performs in opsi-configed. It requires: a
   preceding **preview** (affected clients shown), an explicit
   **confirmation flag** in the request, and it always writes an **audit
   entry**. There is no other write. `opsi-package-updater`
   (download/prepare packages) cannot run over JSON-RPC; in this MVP the
   "prepare packages" action produces the exact server command as a planned
   action (audited), and executing it stays a documented follow-up (SSH
   channel, own dependency decision).
5. **Workflow states** are a module enum covering the full intended
   lifecycle: `Detected`, `UpdateAvailable`, `DownloadNeeded`,
   `PackagePrepared`, `Uploaded`, `ReadyForPilot`, `Approved`,
   `RolloutRequested`, `Completed`, `Failed`. The MVP derives
   `Detected` / `UpdateAvailable` / `RolloutRequested` / `Completed` /
   `Failed` from opsi data; the remaining states exist in the model so the
   package-preparation pipeline can fill them in later without a schema
   break.
6. **Inventory comparison via ADR 0004 contract.** A new Core contract
   `IInstalledSoftwareInventoryProvider` (implemented by the Inventory
   module) exposes stored hosts and their installed software. The Patch
   Management module joins that against opsi products through a
   **manual mapping table** (`patchmanagement_product_mappings`:
   inventory display name ↔ opsi `productId`), editable in the UI. No
   fuzzy auto-matching — a wrong automatic match on a patch tool is worse
   than a manual step; obvious exact-name matches are offered as
   suggestions only.
7. **Audit log** (`patchmanagement_audit_entries`): timestamp, Windows user,
   action, product, target clients/depot, serialized preview data, result,
   error. Every action handler writes one — including failed and merely
   planned actions.
8. **Failure taxonomy:** reuses existing codes where they fit
   (`AUTHENTICATION_FAILED`, `CONNECTION_TIMEOUT`, `ACCESS_DENIED`,
   `DNS_RESOLUTION_FAILED`, `INVALID_REQUEST`) and adds
   `SERVICE_UNAVAILABLE` (opsi service unreachable / TLS rejected /
   non-JSON-RPC answer) and `REMOTE_COMMAND_FAILED` (JSON-RPC-level error
   reported by opsiconfd).

## Consequences

- The module ends WEC's strictly read-only era in one narrow, auditable
  place; ADR 0002 (no elevation) and ADR 0007 (WMI/LDAP credential handling)
  are untouched.
- opsi JSON-RPC coverage means the dashboard works with zero new NuGet
  dependencies; the SSH/`opsi-package-updater` execution path is consciously
  deferred and documented in the UI instead of silently missing.
- Session credentials in memory are a deliberate, bounded relaxation of
  ADR 0007's per-request rule; the bridge still never sends passwords back
  to the WebView.
- The mapping table is the long-term join point for vulnerability data
  (Nessus): a later source can attach CVE/criticality per opsi productId
  without touching the dashboard shape.
- Depot names double as locations (opsi has no separate location concept);
  the default depot filter is an option
  (`Wec:PatchManagement:DefaultDepotFilter`, default `Denkingen` in
  `appsettings.json`, overridable per user like every other tunable).
