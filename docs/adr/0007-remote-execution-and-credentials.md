# ADR 0007: Remote Execution and Credential Handling

- **Status:** Accepted
- **Date:** 2026-07-03
- **Deciders:** Vinz
- **Supersedes:** — (extends ADR 0006 for directory credentials)

## Context

WEC grows from local-only read-only analysis to analyzing remote Windows
clients: inventory, security checks and diagnostics against one or many
machines. That requires two architecture decisions that must hold for years:
how remote data collection is transported, and how credentials are handled.

Constraints:

- Read-only stays read-only: remote access is queries only, no remote writes.
- Modules keep referencing only `Wec.Core`; the transport hides behind the
  existing seams (`IWmiQueryService`, `IDirectoryReader`).
- Expected failures (host down, wrong password, firewall) are `Result` errors
  with distinct codes — never exceptions, never a generic "failed".
- No password may ever be persisted unencrypted.

### Options considered for the remote transport

| Option | Assessment |
|---|---|
| **WSMan-based `CimSession` (Microsoft.Management.Infrastructure)** | Already the local transport; `CimSession.Create(host, WSManSessionOptions)` adds remote with the same query API. WinRM (TCP 5985/5986) is the managed, GPO-deployable enterprise channel. Kerberos/Negotiate, explicit credentials supported. |
| DCOM remote WMI (`System.Management`) | Legacy transport, dynamic RPC ports (firewall-hostile), no future; Microsoft steers to WinRM. |
| PowerShell remoting runspaces | Same WinRM channel but drags a PowerShell host into the process for data CIM already delivers. Wrong dependency weight (same reasoning as ADR 0006 vs RSAT). |
| Own agent/service on targets | Deployment + update burden; contradicts "companion app, no infrastructure". |

### Options considered for credentials

| Option | Assessment |
|---|---|
| **In-memory per request, never persisted** | Simplest correct model. Password lives in the request payload (in-process bridge, no network, ADR 0001), is converted to `SecureString` for the CIM session and dropped afterwards. |
| Windows Credential Manager persistence | Only justified once a "save this credential" feature exists. Adds DPAPI surface now for a feature nobody asked for. |
| Own encrypted store | Reinventing Credential Manager, worse. |

## Decision

1. **Remote transport is WSMan via `CimSession`** in the existing
   `CimWmiQueryService`. Local queries keep using a local session; remote
   targets use `WSManSessionOptions` with the connection timeout from
   options. No DCOM fallback.
2. **Target and credentials become Core types** (`Wec.Core.Targets`):
   `ScanTarget` (Local | Remote host), `ScanCredentials`
   (`CredentialMode.CurrentUser` | `Explicit`), `ConnectionOptions`
   (timeout), `ScanError` (host + phase + error code) for per-host failures
   in multi-target scans. Bridge payloads embed the shared
   `TargetRequest` record; a missing host means local.
3. **Credential policy: in-memory only.** Explicit credentials exist for the
   duration of one request. They are never logged, never persisted, never
   written to disk. If a future feature needs saved credentials, it must use
   the **Windows Credential Manager** (DPAPI) and gets its own ADR revision.
   Explicit credentials for the *local* machine are rejected
   (`UNSUPPORTED_REMOTE_OPERATION`) — local access always uses the invoking
   identity (ADR 0002).
4. **Failure taxonomy** (new `ErrorCode` members, ADR 0003 updated):
   - `DNS_RESOLUTION_FAILED` — own DNS probe before any session attempt,
     because WSMan buries name-resolution errors in generic transport codes.
   - `CONNECTION_TIMEOUT` — WSMan connect/operation timeout.
   - `AUTHENTICATION_FAILED` — WSMan rejected the credentials
     (logon failure / invalid authentication HRESULTs).
   - `ACCESS_DENIED` on a remote target — the account authenticated but lacks
     remote management rights on the target; carries no `requiredPrivilege`
     because elevating the *scanning* machine would not help. Caveat: with
     NTLM, rejected credentials also surface as access denied — the error
     text names that.
   - `WIN_RM_UNAVAILABLE` — WinRM not running or firewall blocks 5985/5986
     (indistinguishable from the client side; the error text names both).
   - `UNSUPPORTED_REMOTE_OPERATION` — a check that can only run locally was
     asked to run against a remote target.
   - Local behavior is unchanged: `ACCESS_DENIED` (+ `requiredPrivilege`),
     `WMI_UNAVAILABLE`.
5. **Remote prerequisites are documentation, not code:** WinRM enabled
   (`winrm quickconfig` / GPO), TCP 5985 (HTTP+SPNEGO) or 5986 (HTTPS),
   the scanning account in the target's `Administrators` (or
   `Remote Management Users` for reduced scope) — documented in the README.
6. **Local-only checks stay local-only and say so.** Checks that depend on
   local APIs (registry seam, event log seam, `System.Net` probes) return
   `UNSUPPORTED_REMOTE_OPERATION` for remote targets until their seam grows
   a remote path. No silent local fallback.

## Consequences

- Modules gain remote capability by passing a target through the existing
  seams — no module references any transport library.
- WinRM being disabled on targets is the common first-run failure; the error
  model makes that a visible, actionable per-host result instead of a batch
  abort.
- Explicit credentials crossing the bridge as strings is acceptable because
  the bridge is in-process (ADR 0001: no HTTP server, no ports); the string
  is short-lived and never serialized to disk. The WebView2 side never
  receives passwords back.
- Kerberos double-hop is irrelevant (one hop, client → target).
- IPv6/workgroup targets may need TrustedHosts on the *scanning* machine for
  NTLM fallback — documented limitation, not handled in code.
