# CLAUDE.md — Windows Enterprise Companion (WEC)

## What this is

Long-term, Windows-only enterprise administration desktop app. NOT a demo.
Built and evolved over many months by a single developer (IT apprentice,
strong sysadmin background: AD, WMI, PowerShell). Treat every decision as
one that must survive years of maintenance.

## Authoritative documents — READ FIRST

Before writing or changing any code, read:

1. `README.md` — current product scope, modules, workflows and operational
   limitations.
2. The README of every module being changed under `src/Modules/`.
3. The relevant accepted records in `docs/adr/`.

This file is the canonical coding-agent instruction source. Accepted ADRs are
binding architecture decisions. `docs/architecture-and-m1-plan.md` is a
historical foundation/M1 plan, not current scope or an implementation queue.
If current code, the product README and an ADR conflict, stop and raise the
conflict instead of silently deviating. New architectural decisions require a
new ADR in `docs/adr/` (numbered, same format).

## Approved architecture (summary — details in the docs above)

- Single .NET 10 Windows process. WinForms shell hosting **WebView2**.
  React + TypeScript + Tailwind (Vite) UI loaded from local assets.
  NO Tauri, NO Electron, NO HTTP server, NO open ports (ADR 0001).
- **Modular monolith.** No runtime plugin loading. Modules implement `IModule`.
- Projects: `Wec.Host` (composition root), `Wec.Core` (contracts, zero deps),
  `Wec.Infrastructure` (EF Core/SQLite, CIM/WMI, privileges, Serilog),
  `Wec.Modules.<Name>` (features).
- **Hard dependency rules (never violate):**
  - `Wec.Core` references nothing (except `Microsoft.Extensions.DependencyInjection.Abstractions`)
  - Modules reference ONLY `Wec.Core` — never Infrastructure, never each other
  - `Wec.Infrastructure` references only `Wec.Core`
  - `Wec.Host` references everything; nothing references it
- IPC: typed JSON envelope over the WebView2 message bridge
  (`{id, module, action, payload}` → `{id, success, data|error}` + events).
  Handlers (`IActionHandler`) are transport-agnostic and return `Result<T>`.
- Elevation (ADR 0002): app starts unelevated (`asInvoker`). `IPrivilegeContext`
  reports capabilities. Never auto-elevate; there is no elevated helper
  process.
- Security separates observed risk from execution truth. `FindingSeverity`
  classifies findings only; persisted per-check `CheckStatus` values
  (`Succeeded`, `Failed`, `RequiresElevation`, `NotApplicable`) determine
  coverage. Incomplete or legacy coverage must never be presented as passed,
  resolved or fully comparable.
- Error model: `Result<T>` with typed `Error {Code, Message, Details}` for
  expected failures (access denied, WMI unavailable, not found).
  Exceptions are reserved for bugs; a global handler maps them to a generic
  `INTERNAL_ERROR` envelope without leaking internals.
- Remote analysis (ADR 0007): WSMan `CimSession` behind `IWmiQueryService`,
  LDAP credentials behind `IDirectoryReader` (ADR 0006 revision). Targets and
  credentials are Core types (`Wec.Core.Targets`); explicit credentials are
  in-memory per request, never persisted. Local-only work reports a typed
  `UNSUPPORTED_REMOTE_OPERATION`/`NotApplicable` result instead of silently
  falling back to the WEC machine.
- Patch management (ADR 0008/0017): opsi depot/client data is read through
  `IOpsiClient`; Winget discovery uses the typed `IWingetCatalogClient`. Confirmed
  package sources are generated locally, transferred by strict SCP, built and
  installed on one depot through `IRemoteCommandExecutor`, then verified through
  opsi. WEC never creates client action requests or synchronizes depots. Every
  catalog check and build is audited; opsi and SSH passwords are never persisted.
- Persistence: one `WecDbContext` (Infrastructure) + SQLite at
  `%LOCALAPPDATA%\Wec\wec.db`. EF Core migrations from day 1, applied at startup.
  Module entity configurations (`IEntityTypeConfiguration<T>`) live in the module,
  discovered via assembly scanning. Tables are module-prefixed
  (`inventory_hardware_snapshots`).
- Configuration: `appsettings.json` defaults + `%APPDATA%\Wec\usersettings.json`
  overrides. Options pattern with `ValidateOnStart`. **No hardcoded tunables** —
  cache TTLs, paths, log levels are all options.
- Logging: Serilog, structured. Rolling file in `%LOCALAPPDATA%\Wec\logs\`,
  console sink in DEBUG. Every bridge request carries a `CorrelationId`
  (= envelope id) plus `Module` and `Action` properties.
- Module-facing frontend features mirror the corresponding backend module;
  frontend-only workspaces such as Dashboard and Clients may compose multiple
  module contracts. `shared/bridge/bridgeClient.ts` handles invoke/subscribe
  with id correlation and timeouts. `shared/api-types.ts` mirrors C# DTOs
  (manual sync for now).

## Current product state

M1 is complete. The host currently registers Inventory, Security, Diagnostics,
Reporting, Active Directory, Patch Management, Print Management, Network Scan,
Saved Targets, IT Lifecycle and Vulnerability Management. The React shell also
contains the Dashboard, Clients workspace, Settings and Error Log views. Use
`README.md`, the relevant module README and current code as the product-state
reference; do not infer the next milestone from the historical M1 plan.

Keep the established modular-monolith boundaries. Do not introduce runtime
plugins, dynamic navigation, a local HTTP server, merged modules, per-feature
architecture-layer projects, cross-module feature references, generic
repositories or generic check/diagnostic base classes, MediatR/CQRS, an event
bus or global React state without an explicitly approved architectural
requirement and ADR.

## Coding conventions

- C#: .NET 10, `Nullable` enabled, `TreatWarningsAsErrors`, analyzers on,
  Central Package Management (`Directory.Packages.props`).
- **Self-explanatory names instead of comments.** Functions, classes, and
  variables must be understandable from their name alone. Comments only for
  *why*, never for *what*.
- SOLID, composition over inheritance, interfaces at seams (WMI, PowerShell,
  repositories, clock/time).
- Tests: xUnit + NSubstitute. Unit tests mock Core abstractions
  (`IWmiQueryService` etc.). Integration tests run real migrations against a
  temp SQLite **file** (never the EF in-memory provider). Frontend: Vitest +
  Testing Library.
- Every module gets a README. Architectural decisions get ADRs.

## How to work with the developer

- Language: respond in German (Hochdeutsch). Code, identifiers, commits,
  and docs in English.
- Be direct and critical. Challenge decisions that create technical debt.
  Do not praise; verify correctness.
- Compare alternatives before implementing when multiple good options exist;
  explain the *why* of significant design decisions briefly.
- **Actively flag overengineering.** The developer has a known tendency to
  build infrastructure before core functionality. If a task drifts toward
  premature abstraction, generic frameworks, or Phase 2+ concerns, stop and
  say so.
- Prefer the simplest solution that satisfies the architecture. Maintainability
  over short-term speed, but also over speculative flexibility.
- Teach while building: this project doubles as training in enterprise
  software development. Short explanations of patterns/best practices at the
  point where they are applied are welcome — no lectures.

## Commands

```bash
dotnet build                      # full solution build
dotnet test                       # all backend tests
cd frontend && npm run dev        # Vite dev server (WebView2 points here in DEBUG)
cd frontend && npm run build      # production assets → copied to Wec.Host/wwwroot
```

Keep this file updated when foundational decisions change (new ADR ⇒ update summary here).
