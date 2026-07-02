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
