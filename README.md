# Windows Enterprise Companion (WEC)

Windows-only enterprise administration desktop app. Single .NET 10 process:
WinForms shell hosting **WebView2**, React + TypeScript + Tailwind UI served
from local assets, typed JSON message bridge — no HTTP server, no open ports
(ADR 0001). Modular monolith; modules implement `IModule` and reference only
`Wec.Core`.

Assessment features are read-only and target the local machine, remote Windows
clients or connected management systems. Two workflows deliberately write to
managed systems:
Patch Management can submit confirmed, previewed and audited opsi/package
operations (ADRs 0008, 0015 and 0016), and Print Management can delete only
ports that were detected as unused after explicit confirmation. Each backend
module has its own README under `src/Modules/`.

User-configurable operating parameters belong in the in-app **Settings** area.
`appsettings.json` provides deployment defaults; UI changes are merged into
`%APPDATA%\Wec\usersettings.json` and never include passwords or session
credentials. New modules should extend the existing Settings surface instead
of introducing module-specific configuration files or hidden editors.

The primary workspace is **Clients** (ADR 0010): an Active-Directory-sourced
client list (unpopulated until opened) where a client is scanned on demand —
Inventory, Security, Diagnostics and installed Printers as tabbed sections that
share one session credential per host — and two clients can be compared. The
sidebar keeps the fleet-wide **Dashboard**, Active Directory, IT Lifecycle,
Vulnerabilities, Patch Management, Print Management, Network Scan and
Reporting views, plus Settings and Error Log under **Verwaltung**. Frequently
used servers (print server, opsi, DC) can be saved as **targets** (host, role
and user name, never a password) and pre-fill each picker. The UI follows a
shared design system
([`frontend/src/shared/ui`](frontend/src/shared/ui/README.md)): bundled
Inter (UI) and JetBrains Mono (serials/IPs/versions) fonts, semantic color
tokens (`accent` + `ok/warn/fail/info`), and shared primitives — page
headers, buttons, form controls, dot+label badges, summary metrics, data
tables (zebra, sticky header, aligned/monospaced columns) and empty/error
states. Every result view names the host, scan status and timestamp it
belongs to; results for a previously selected target are never shown next
to a newly selected one.

Current product scope is maintained in this README and the module READMEs.
Accepted architecture decisions are in [docs/adr/](docs/adr/). The
[foundation/M1 plan](docs/architecture-and-m1-plan.md) is retained as a
historical record and is not the current implementation plan. `Claude.md` is
the canonical coding-agent instruction source.

## Modules

| Module | Scope | Remote |
|---|---|---|
| Inventory | CPU, RAM, disks, OS, network adapters, GPUs, monitors, installed software and BitLocker; one persistent snapshot per host | yes (software via registry/StdRegProv) |
| Security | 13 read-only checks with persisted per-check execution outcome, coverage and host-scoped history; incomplete coverage is never a clean scan | yes (registry checks via StdRegProv; some checks are explicitly local-only) |
| Diagnostics | Persisted latest troubleshooting run per host: network, DNS, domain, time, services, event log and system state | machine-state checks yes; connectivity/event-log probes stay local-perspective |
| Active Directory | Domain overview and hygiene over LDAP; test bind; computer search for the Clients workspace and user search for IT Lifecycle | own or explicitly named domain/DC |
| IT Lifecycle | Read-only correlation of AD computers with Kaspersky Security Center inventory for missing, orphaned, stale or outdated agents/endpoints | AD/LDAP + KSC OpenAPI |
| Vulnerability Management | Nessus scan import with persisted assets/findings, sync status and historical trend | Nessus API (HTTPS :8834 by default) |
| Patch Management | Central opsi package/depot dashboard (ADR 0008/0014/0015): manufacturer checks, depot comparison, gated test → approval → synchronization, client rollout and versioned audit history | opsi JSON-RPC (HTTPS :4447) + Windows OpenSSH for confirmed package operations |
| Print Management | Printer inventory per print server with SNMP device data (serial, model, location, status, toner levels); queues merged per physical device, search + site grouping, snapshot history with lease-swap diff, CSV export, device web-UI links (ADR 0009). Client-installed printers are a separate CIM path shown in the client detail | print servers over WinRM; devices over SNMP v2c (UDP 161, read-only) |
| Network Scan | Active nmap discovery, device classification, reverse DNS and optional DHCP reservation correlation | scanned network ranges + optional DHCP server |
| Reporting | HTML/JSON executive summary per machine (local or a scanned remote client); reads already-captured data, never starts a scan | local + any scanned client |
| Saved Targets | Persist frequently used servers/clients (host + role + user name, never a password) to pre-fill the pickers (ADR 0010) | local (SQLite) |
| Clients | AD-sourced client workspace; on-demand per-client scans, compare, on-demand online status (ping + WinRM 5985), one-click PowerShell remoting session (ADR 0011) | remote clients over WinRM |
| Verwaltung | App-wide configuration and an error log reading warnings/errors from the current log file | local |

Admin credentials are entered once (top-bar sign-in) and reused for every
remote target; the password lives in memory only, never persisted or logged
(ADR 0007/0011). opsi keeps its own session login. Nessus API keys are stored
separately in Windows Credential Manager and never in user settings or SQLite.

## Prerequisites

- Windows 11 with the WebView2 Evergreen Runtime (preinstalled on current builds)
- .NET SDK 10.0.3xx (pinned in `global.json`)
- Node.js ≥ 20 + npm (frontend build)

## Installation

Every green CI run on `master` produces two artifacts (ADR 0005), both
self-contained for win-x64 — no .NET runtime needed on the target machine:

- `wec-<version>-win-x64.zip` — portable; unzip anywhere and run
  `Wec.Host.exe`.
- `wec-<version>-setup.exe` — per-user installer to
  `%LOCALAPPDATA%\Programs\Wec` (**no administrator rights required**),
  with Start menu entry and uninstaller. Uninstalling keeps the runtime
  data in `%LOCALAPPDATA%\Wec` (database, logs).

Artifacts are not code-signed yet, so SmartScreen warns on first run of
downloaded builds.

## Build & run

```bash
cd frontend && npm install && npm run build   # emits into src/Wec.Host/wwwroot
cd .. && dotnet build
dotnet run --project src/Wec.Host
```

The app starts **unelevated** by design (ADR 0002). Checks that need more
rights render a "Requires elevation" status instead of failing silently.

## Unelevated behavior

- The manifest requests `asInvoker`; the app never auto-elevates.
- The sidebar footer shows the current privilege level
  (`Standard user` / `Administrator`) reported by `IPrivilegeContext`.
- Checks declare their required privilege. Unelevated, the BitLocker card on
  the Inventory page shows an amber **Requires elevation** badge — this is
  expected, not an error. To run those checks, use **Restart as
  administrator** in the sidebar footer: it starts an elevated copy via the
  regular UAC prompt and closes the unelevated instance. The app still never
  elevates itself (ADR 0002) — dismissing the UAC prompt simply keeps the
  current instance running.
- Access-denied results always carry the required privilege in the typed
  error envelope (`ACCESS_DENIED` + `requiredPrivilege`, ADR 0003).

## Verifying the inventory cache

Hardware data is cached in SQLite with a TTL (`Wec:Inventory:CacheTtl`,
default 15 minutes):

1. Start the app — the Inventory header shows **Freshly captured** with a
   timestamp; the log records `Captured fresh hardware snapshot`.
2. Restart within the TTL — the header shows **From cache** with the *same*
   timestamp; the log records `Serving hardware snapshot from cache`.
3. **Refresh** forces a new WMI capture regardless of TTL.

Logs (rolling daily, path shown in the sidebar footer) carry a
`CorrelationId` per bridge request for tracing a UI action end to end.

## Remote analysis

Inventory, Security and the WMI-based Diagnostics reach remote clients over
**WinRM** (WSMan CimSession, including StdRegProv registry reads for the
software list and reboot-pending signals); Active Directory analysis uses
**LDAP**. Requirements on the *target* machines:

- WinRM enabled (`winrm quickconfig`, or the "Allow remote server management
  through WinRM" GPO) and **TCP 5985** (HTTP + SPNEGO-encrypted) or
  **TCP 5986** (HTTPS) open in the firewall.
- The scanning account must be in the target's `Administrators` group (most
  WMI namespaces require it remotely); `Remote Management Users` works for
  reduced scope.
- AD analysis needs **TCP 389** to a DC and DNS servers that know the domain.

Credential behavior (ADR 0007): scans run as the **current user** by default
(Kerberos/Negotiate). Explicit credentials can be entered per scan; they live
in memory for the duration of that request only — never logged, never
persisted. Expected failures surface as typed per-host errors
(`DNS_RESOLUTION_FAILED`, `CONNECTION_TIMEOUT`, `AUTHENTICATION_FAILED`,
`WIN_RM_UNAVAILABLE`, …), and checks that can only run locally say so
instead of being skipped silently.

Known limitations: workgroup targets may require WinRM `TrustedHosts` on the
scanning machine (NTLM fallback); firewall-blocked and service-stopped WinRM
are indistinguishable from the client side (one combined error message).
Rejected credentials report `AUTHENTICATION_FAILED`, missing rights on the
target report `ACCESS_DENIED` — except under NTLM, where WinRM reports both
as access denied (the error text says so).

## opsi package operations over SSH

Confirmed package updates use the Windows OpenSSH client and never store an
SSH password. Before the first run:

1. Install the Windows **OpenSSH Client** optional feature.
2. Add every opsi depot host key to the current Windows user's
   `~/.ssh/known_hosts` and verify its fingerprint out of band.
3. Configure agent/default-key authentication for
   `Wec:PatchManagement:SshUserName`, or set `SshIdentityFile` in the per-user
   settings file.
4. If the SSH account is not root, enable `UseNonInteractiveSudo` and grant a
   narrowly scoped passwordless sudo rule for `opsi-package-updater`.

The command timeout defaults to 30 minutes. WEC verifies the resulting
`productOnDepot` version through opsi before allowing pilot approval.

## Current limitations

- The executive-summary report is per machine and reads only data already
  captured for that host (there is no multi-host aggregate report yet).
  Diagnostics connectivity probes (gateway, DNS, DC reachability) and the
  event-log summary always measure from the machine WEC runs on and are
  visibly skipped for remote targets; machine-state diagnostics (domain
  membership, reboot pending, time sync, disks, services, updates) run against
  the remote target.
- Security scans written before persisted per-check coverage was introduced
  remain visibly coverage-unknown. They are not treated as fully comparable in
  history and cannot produce resolved claims.
- Remote software inventory reads the uninstall keys through WMI StdRegProv —
  it needs an account with remote registry read rights and takes noticeably
  longer than a local read (one WinRM round trip per registry value).
- The snapshot store keeps one snapshot per host (no history); hosts stay
  listed until deleted or rescanned.
- Elevation applies to the whole app via restart (button in the sidebar
  footer); there is no per-action elevation prompt (deliberate, ADR 0002).
- Patch Management executes explicitly confirmed repository-backed
  `opsi-package-updater` runs over Windows OpenSSH (ADR 0015). SSH uses
  key/agent authentication, strict host-key checking and a test-depot approval
  gate; package operations and rollout requests are audited per target.
- TypeScript API types are mirrored manually from the C# DTOs
  (`frontend/src/shared/api-types.ts`) — review on every DTO change.
- Artifacts are not code-signed (no certificate yet) — SmartScreen warns on
  first run of downloaded builds. See
  [ADR 0005](docs/adr/0005-packaging-and-distribution.md).

## Development workflow

Frontend hot reload: run `npm run dev` in `frontend/`, then point the host at
the dev server via `%APPDATA%\Wec\usersettings.json`:

```json
{ "Wec": { "Frontend": { "UseDevServer": true } } }
```

Backend tests:

```bash
dotnet test
```

New EF Core migration (tool is pinned in the local tool manifest):

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/Wec.Infrastructure --startup-project src/Wec.Host --output-dir Persistence/Migrations
```

Migrations are applied automatically at app startup.

## Continuous integration

[.github/workflows/ci.yml](.github/workflows/ci.yml) runs on every push to
`master` and every pull request (Windows runner — the host targets
`net10.0-windows`): frontend tests + build, backend build with
`TreatWarningsAsErrors`, all backend tests. When every gate passed on a push
to `master`, CI publishes the host **self-contained for win-x64** (no .NET
runtime needed on target machines, ADR 0005) and uploads a portable
`wec-<version>-win-x64.zip` plus a per-user Inno Setup installer
(`packaging/wec-installer.iss`).

## Releasing

Releases are cut by pushing a version tag; CI runs the identical gates and
attaches both artifacts to a GitHub release with generated notes:

1. Bump `<Version>` in `Directory.Build.props`, commit to `master`.
2. `git tag v<version> && git push origin v<version>`

The workflow refuses tags that do not match the project version, so a
mislabeled release cannot be published.

## Runtime locations

| What | Where |
|---|---|
| Database (SQLite) | `%LOCALAPPDATA%\Wec\wec.db` |
| Logs (Serilog, rolling daily) | `%LOCALAPPDATA%\Wec\logs\` |
| WebView2 profile | `%LOCALAPPDATA%\Wec\webview2\` |
| User settings overrides | `%APPDATA%\Wec\usersettings.json` |

All tunables (cache TTL, paths, log level) are options — defaults in
`src/Wec.Host/appsettings.json`, overridable per user.

## Solution layout

| Project | Role |
|---|---|
| `src/Wec.Host` | WinForms shell, WebView2, bridge dispatcher, DI composition root |
| `src/Wec.Core` | Contracts only: `Result<T>`, envelopes, `IModule`, abstractions |
| `src/Wec.Infrastructure` | EF Core/SQLite, CIM/WMI, privilege detection, Serilog |
| `src/Modules/Wec.Modules.*` | Feature modules; each owns its handlers, domain/application logic, persistence configuration where needed, and README |
| `frontend/` | Vite + React + Tailwind; `features/<x>` mirrors `Wec.Modules.<X>` |
| `tests/` | xUnit unit tests + SQLite file-based integration tests |

Dependency rules (build-enforced): modules and Infrastructure reference only
Core; Host references everything; nothing references Host.
