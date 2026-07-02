# WEC — Solution Structure, Dependency Rules, M1 Plan

Status: Draft for approval · 2026-07-02
Scope: expensive-to-change foundations + Milestone 1. Phases 2–5 evolve via ADRs.

---

## 1. Solution & Repository Structure

```
WindowsEnterpriseCompanion/
├── WindowsEnterpriseCompanion.sln
├── Directory.Build.props              # LangVersion, Nullable, TreatWarningsAsErrors, analyzers
├── Directory.Packages.props           # Central Package Management (one version per package)
├── .editorconfig                      # Coding conventions, enforced by analyzers
├── global.json                        # Pinned .NET SDK version
│
├── src/
│   ├── Wec.Host/                      # WinForms shell + WebView2 + composition root
│   │   ├── Program.cs                 # Generic Host bootstrap, DI wiring, Serilog init
│   │   ├── MainWindow.cs              # Thin: hosts WebView2 control only
│   │   ├── Bridge/                    # WebMessage receiver, envelope router, dispatcher
│   │   └── wwwroot/                   # Built frontend assets (Vite output, copied on build)
│   │
│   ├── Wec.Core/                      # Contracts only. Zero external dependencies*
│   │   ├── Modules/                   #   IModule, ModuleDescriptor
│   │   ├── Messaging/                 #   BridgeRequest, BridgeResponse, BridgeEvent, IActionHandler
│   │   ├── Results/                   #   Result<T>, Error, ErrorCode, CheckStatus
│   │   ├── Privileges/                #   IPrivilegeContext, PrivilegeLevel
│   │   └── Abstractions/              #   IClock, IWmiQueryService, IPowerShellRunner (interfaces only)
│   │
│   ├── Wec.Infrastructure/            # Implementations of Core abstractions
│   │   ├── Persistence/               #   WecDbContext, migrations, repositories
│   │   ├── Wmi/                       #   CIM-based WmiQueryService (Microsoft.Management.Infrastructure)
│   │   ├── Privileges/                #   WindowsPrivilegeContext
│   │   └── Logging/                   #   Serilog configuration, in-app sink (later)
│   │
│   └── Modules/
│       └── Wec.Modules.Inventory/     # First module (M1)
│           ├── InventoryModule.cs     #   IModule implementation, service + handler registration
│           ├── Application/           #   HardwareInfoService (business logic)
│           ├── Domain/                #   HardwareSnapshot, Cpu, MemoryBank, Disk (POCOs)
│           ├── Handlers/              #   GetHardwareInfoHandler (bridge action)
│           └── Persistence/           #   IHardwareSnapshotRepository + EF configuration
│
├── frontend/
│   ├── package.json / vite.config.ts / tailwind.config.ts / tsconfig.json
│   └── src/
│       ├── app/                       # Shell, router, layout, navigation
│       ├── shared/
│       │   ├── bridge/                # bridgeClient.ts (request/response correlation, events)
│       │   ├── api-types.ts           # Mirrors C# DTOs (manual for now, see §5)
│       │   └── ui/                    # Base components (Card, StatusBadge, Table)
│       └── features/
│           └── inventory/             # HardwareInfoPage, hooks, feature-local components
│
├── tests/
│   ├── Wec.Core.Tests/                # Result<T>, envelope serialization
│   ├── Wec.Modules.Inventory.Tests/   # Unit tests, mocked IWmiQueryService
│   └── Wec.Infrastructure.IntegrationTests/  # SQLite file-based, real migrations
│
└── docs/
    ├── adr/
    │   ├── 0001-webview2-host-instead-of-tauri.md
    │   ├── 0002-conservative-elevation-strategy.md
    │   └── 0003-ipc-envelope-contract.md          # written during M1
    └── architecture-and-m1-plan.md                # this document
```

\* `Wec.Core` may reference `Microsoft.Extensions.DependencyInjection.Abstractions`
(for `IServiceCollection` in `IModule`). Nothing else.

## 2. Project List

| Project | Type | Purpose | Allowed references |
|---|---|---|---|
| `Wec.Host` | WinForms exe (net9.0-windows) | Shell, WebView2, DI composition root, bridge router | Core, Infrastructure, all Modules |
| `Wec.Core` | classlib (net9.0) | Contracts, Result model, module/bridge/privilege abstractions | DI.Abstractions only |
| `Wec.Infrastructure` | classlib (net9.0-windows) | EF Core/SQLite, WMI/CIM, privilege detection, Serilog setup | Core |
| `Wec.Modules.Inventory` | classlib (net9.0-windows) | Inventory feature: logic, domain, handlers, repository interface + EF config | Core |
| `Wec.Core.Tests` | xunit | Contract & Result tests | Core |
| `Wec.Modules.Inventory.Tests` | xunit | Module logic with mocked abstractions | Inventory, Core |
| `Wec.Infrastructure.IntegrationTests` | xunit | Real SQLite persistence, migration validation | Infrastructure, Inventory, Core |

WinForms over WPF for the shell: the shell contains exactly one control (WebView2)
and no native UI. WinForms is the thinner host with less ceremony. If native
windows chrome/theming ever matters, swapping the shell is cheap because it
contains no logic.

## 3. Dependency Graph & Rules

```
                ┌─────────────────────┐
                │      Wec.Host       │   composition root
                └───┬─────┬───────┬───┘
                    │     │       │
        ┌───────────▼┐  ┌─▼───────▼─────────┐
        │ Wec.Infra-  │  │ Wec.Modules.*    │
        │ structure   │  │ (Inventory, ...) │
        └───────┬─────┘  └───────┬──────────┘
                │                │
                └───────┬────────┘
                        ▼
                ┌───────────────┐
                │   Wec.Core    │   contracts, no dependencies
                └───────────────┘

frontend/features/<x>  ──mirrors──  src/Modules/Wec.Modules.<X>
```

Hard rules (enforced by project references; violations are build errors):

1. `Wec.Core` references nothing (except DI abstractions).
2. Modules reference **only** `Wec.Core`. Never Infrastructure, never each other.
3. `Wec.Infrastructure` references only `Wec.Core`.
4. `Wec.Host` is the only project that references everything; nothing references it.
5. Cross-module communication (when it becomes necessary) goes through
   contracts/events defined in `Wec.Core` — decided per case, own ADR when Phase 2 starts.

Consequence of rule 2: a module defines *interfaces* for what it needs
(e.g. `IHardwareSnapshotRepository`) or uses Core abstractions
(`IWmiQueryService`); Infrastructure or the module's own Persistence folder
implements them, Host wires them. This keeps modules unit-testable with zero
infrastructure.

EF Core note: module EF entity configurations live in the module
(`IEntityTypeConfiguration<T>`), the single `WecDbContext` in Infrastructure
discovers them via assembly scanning at startup. One database, one migration
history, module-prefixed tables (`inventory_hardware_snapshots`).

## 4. IPC Contract (summary — full version becomes ADR 0003 during M1)

Envelope over `window.chrome.webview.postMessage` / `PostWebMessageAsJson`:

```jsonc
// Frontend → Host
{ "id": "uuid", "module": "inventory", "action": "getHardwareInfo", "payload": { } }

// Host → Frontend (response, correlated by id)
{ "id": "uuid", "success": true,  "data": { } }
{ "id": "uuid", "success": false, "error": { "code": "ACCESS_DENIED", "message": "...", "requiredPrivilege": "Administrator" } }

// Host → Frontend (unsolicited event)
{ "type": "event", "module": "inventory", "event": "scanProgress", "payload": { "percent": 40 } }
```

- Routing: `module` + `action` → registered `IActionHandler` (dispatcher in Host/Bridge).
- Handlers are transport-agnostic: they receive a typed request, return `Result<T>`.
  The bridge layer serializes. (Keeps a future Kestrel transport cheap — ADR 0001.)
- `bridgeClient.ts` provides `invoke<TReq, TRes>(module, action, payload)` returning
  a Promise, correlation via `id`, timeout handling, and `subscribe(module, event, cb)`.

## 5. Cross-Cutting Decisions (M1-relevant)

| Concern | Decision |
|---|---|
| DI | Generic Host + `Microsoft.Extensions.DependencyInjection`. Each module self-registers via `IModule.RegisterServices`. |
| Logging | Serilog. Sinks: rolling file in `%LOCALAPPDATA%\Wec\logs\`, console in DEBUG. Structured properties: `Module`, `Action`, `CorrelationId` (= bridge request id). |
| Configuration | `appsettings.json` (shipped defaults) + `%APPDATA%\Wec\usersettings.json` (user overrides). Options pattern, `ValidateOnStart`. No literals in code — every tunable is an option. |
| Error model | `Result<T>` with typed `Error { Code, Message, Details }` for expected failures (access denied, WMI unavailable, not found). Exceptions = bugs; global handler logs + returns generic `INTERNAL_ERROR` envelope without leaking internals. |
| Check status | `CheckStatus: Succeeded / Failed / RequiresElevation / NotApplicable` (ADR 0002). |
| DB | EF Core + SQLite, migrations from day 1, DB at `%LOCALAPPDATA%\Wec\wec.db`. Migrations applied at startup (single-user desktop app — acceptable; revisit if that assumption changes). |
| TS types | `shared/api-types.ts` maintained manually and reviewed against C# DTOs. Generator (TypeGen/NSwag) only when DTO count makes manual sync error-prone (~10+). |
| Tests | xUnit + NSubstitute (backend), Vitest + Testing Library (frontend, minimal in M1). Integration tests run real migrations against a temp SQLite file — not in-memory provider, because SQLite behavior (types, constraints) is part of what we test. |
| Naming | Self-explanatory identifiers; comments only for *why*. Analyzers + `.editorconfig` enforce style; `TreatWarningsAsErrors` on. |

## 6. M1 Implementation Plan

**Goal:** "Show hardware information for the local machine" — end to end.
**Flow:** CIM query → Inventory module → SQLite cache → bridge → React panel.

### Steps (each ends in a commit; order matters)

| # | Step | Deliverable | Validates |
|---|---|---|---|
| 1 | Repo skeleton | Solution, projects, Directory.*.props, .editorconfig, empty test projects, CI-ready build | Dependency rules compile-enforced |
| 2 | Core contracts | `Result<T>`, `Error`, `ErrorCode`, envelope records, `IModule`, `IActionHandler`, `IPrivilegeContext`, `IWmiQueryService` | Error model design on paper → in types |
| 3 | Host bootstrap | Generic Host, Serilog, options loading + validation, WinForms window with WebView2 loading a static placeholder page | Process model, config, logging |
| 4 | Bridge | Envelope router/dispatcher in Host, `bridgeClient.ts`, round-trip "ping" action | IPC contract ergonomics → write ADR 0003 |
| 5 | Frontend shell | Vite + React + Tailwind, layout, routing, inventory page stub wired to bridge | Build/asset pipeline into `wwwroot` |
| 6 | Infrastructure | `WecDbContext` + first migration (`inventory_hardware_snapshots`), CIM-based `WmiQueryService`, `WindowsPrivilegeContext` | EF startup cost, CIM API fit |
| 7 | Inventory module | Domain types, `HardwareInfoService` (query → snapshot → cache with TTL), `GetHardwareInfoHandler`, module registration | Module boundary works in practice |
| 8 | Failure path | Access-denied mapping → `Result` failure with `RequiresElevation`; UI `StatusBadge` renders it distinctly | ADR 0002 end to end |
| 9 | Tests | Unit: `HardwareInfoService` with mocked `IWmiQueryService` (success, WMI failure, cache-hit). Integration: snapshot persisted and reloaded through real migration | Test strategy is real, not aspirational |
| 10 | Docs | ADR 0003 final, module README, root README (build/run) | Documentation habit from day 1 |

### M1 Definition of Done

- [ ] App starts unelevated, shows hardware info (CPU, RAM, disks, OS) in React UI
- [ ] Data cached in SQLite; second load served from cache with visible timestamp + refresh action
- [ ] One expected-failure path produces a typed error rendered as a clear UI status
- [ ] `IPrivilegeContext` reports elevation state; at least one path returns `RequiresElevation`
- [ ] Structured logs with correlation id per bridge request
- [ ] ≥1 unit test (module logic), ≥1 integration test (SQLite persistence), all green in `dotnet test`
- [ ] Zero hardcoded tunables (cache TTL, paths, log level are options)
- [ ] ADRs 0001–0003 committed

### Known risks going into M1

| Risk | Mitigation |
|---|---|
| WebView2 bridge ergonomics worse than expected (large payloads, ordering) | Step 4 is deliberately early; if the bridge fights us, pivot to Kestrel variant before any module code exists |
| `Microsoft.Management.Infrastructure` (CIM) quirks vs legacy `System.Management` | Step 6 spikes both behind `IWmiQueryService`; interface hides the choice |
| EF Core cold-start latency on app launch | Measure in step 6; compiled models or lazy migration if >500 ms |
| Vite dev workflow vs packaged wwwroot divergence | Dev mode: WebView2 points at Vite dev server; release: local files. Both paths built in step 5 |

### Explicitly out of scope for M1

Runtime plugin loading · elevated helper process · TS type generator ·
in-app log viewer · dashboard module · any Phase 2+ concern.
