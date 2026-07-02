# CLAUDE.md — Windows Enterprise Companion (WEC)

## What this is

Long-term, Windows-only enterprise administration desktop app. NOT a demo.
Built and evolved over many months by a single developer (IT apprentice,
strong sysadmin background: AD, WMI, PowerShell). Treat every decision as
one that must survive years of maintenance.

## Authoritative documents — READ FIRST

Before writing or changing any code, read:

1. `docs/architecture-and-m1-plan.md` — solution structure, project list,
   dependency rules, IPC contract sketch, cross-cutting decisions,
   M1 step-by-step plan with Definition of Done.
2. `docs/adr/0001-webview2-host-instead-of-tauri.md`
3. `docs/adr/0002-conservative-elevation-strategy.md`

These documents are binding. If an implementation detail conflicts with them,
stop and raise the conflict instead of silently deviating. New architectural
decisions require a new ADR in `docs/adr/` (numbered, same format).

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
  reports capabilities. Checks return
  `CheckStatus: Succeeded | Failed | RequiresElevation | NotApplicable`.
  Never auto-elevate. No elevated helper process in M1.
- Error model: `Result<T>` with typed `Error {Code, Message, Details}` for
  expected failures (access denied, WMI unavailable, not found).
  Exceptions are reserved for bugs; a global handler maps them to a generic
  `INTERNAL_ERROR` envelope without leaking internals.
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
- Frontend mirrors backend: `frontend/src/features/<x>` ↔ `Wec.Modules.<X>`.
  `shared/bridge/bridgeClient.ts` handles invoke/subscribe with id correlation
  and timeouts. `shared/api-types.ts` mirrors C# DTOs (manual sync for now).

## Current milestone: M1 — "Show hardware information for the local machine"

Flow: CIM query → Inventory module → SQLite cache → bridge → React panel.
Follow the 10-step plan in `docs/architecture-and-m1-plan.md` §6 **in order**,
one commit per step. Do not skip ahead. The Definition of Done in that document
is the acceptance criteria.

Out of scope for M1 (do not build, even if "it would be easy"):
runtime plugin loading, elevated helper process, TypeScript type generator,
in-app log viewer, dashboard module, anything from Phase 2+.

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
  Testing Library (minimal in M1).
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

## Commands (once the skeleton exists)

```bash
dotnet build                      # full solution build
dotnet test                       # all backend tests
cd frontend && npm run dev        # Vite dev server (WebView2 points here in DEBUG)
cd frontend && npm run build      # production assets → copied to Wec.Host/wwwroot
```

Keep this file updated when foundational decisions change (new ADR ⇒ update summary here).
