# Windows Enterprise Companion (WEC)

Windows-only enterprise administration desktop app. Single .NET 9 process:
WinForms shell hosting **WebView2**, React + TypeScript + Tailwind UI served
from local assets, typed JSON message bridge — no HTTP server, no open ports
(ADR 0001). Modular monolith; modules implement `IModule` and reference only
`Wec.Core`.

Authoritative docs: [docs/architecture-and-m1-plan.md](docs/architecture-and-m1-plan.md)
and the ADRs in [docs/adr/](docs/adr/).

## Prerequisites

- Windows 11 with the WebView2 Evergreen Runtime (preinstalled on current builds)
- .NET SDK 9.0.3xx (pinned in `global.json`)
- Node.js ≥ 20 + npm (frontend build)

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
  expected, not an error. To run those checks, close the app and start it
  again via *Run as administrator* (manual, per ADR 0002).
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

## Current limitations

- Local machine only — no remote inventory, no domain/AD features yet.
- Inventory covers CPU, memory banks, physical disks, OS and BitLocker
  status; no monitors, GPUs, network adapters or installed software.
- The snapshot cache keeps only the latest snapshot (no history).
- Elevation requires a manual restart as administrator; there is no
  per-action elevation prompt (deliberate, ADR 0002).
- TypeScript API types are mirrored manually from the C# DTOs
  (`frontend/src/shared/api-types.ts`) — review on every DTO change.
- No installer/packaging and no CI pipeline yet (roadmap M8/M9).

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
| `src/Modules/Wec.Modules.Inventory` | First feature module (see its [README](src/Modules/Wec.Modules.Inventory/README.md)) |
| `frontend/` | Vite + React + Tailwind; `features/<x>` mirrors `Wec.Modules.<X>` |
| `tests/` | xUnit unit tests + SQLite file-based integration tests |

Dependency rules (build-enforced): modules and Infrastructure reference only
Core; Host references everything; nothing references Host.
