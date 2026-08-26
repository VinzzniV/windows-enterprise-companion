# AGENTS.md — Windows Enterprise Companion (WEC)

## What this is

Long-term, Windows-only enterprise administration desktop app. NOT a demo.
Built and evolved over many months by a single developer (IT apprentice,
strong sysadmin background: AD, WMI, PowerShell). Treat every decision as
one that must survive years of maintenance.

## Authoritative documents — READ FIRST

Before writing or changing any code, read:

1. `ROADMAP_DECISIONS.md` — confirmed product, privacy, execution and release
   boundaries.
2. `ROADMAP_EXECUTION.md` — current phase, slice, blockers and last verified
   baseline.
3. `docs/ultimate-admin-tool-roadmap.md` — ordered product and implementation
   roadmap.
4. `docs/architecture-and-m1-plan.md` — original structure and dependency
   rules. Its M1 sequence is historical, not the current milestone.
5. All accepted ADRs relevant to the current slice, especially ADR 0001–0007
   and any roadmap ADRs added later.

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
  and timeouts. C# bridge DTOs generate
  `frontend/src/shared/api-types.generated.ts`; CI verifies the generated file.
  `shared/api-types.ts` is only a compatibility facade for stable UI names.

## Current program

The active program is `docs/ultimate-admin-tool-roadmap.md`. Follow its phases
and slices in order unless a verified dependency requires a documented reorder.
The original M1 is complete and remains architectural history.

Do not start roadmap implementation without an explicit user start instruction.
At the start, create a `codex/...` branch and record the active slice in
`ROADMAP_EXECUTION.md`. Keep each commit reviewable and green. Update the
execution file at phase boundaries, genuine blockers and completed slices —
not as a verbose activity log.

Out of scope unless a new explicit decision and ADR authorize it: server mode,
runtime plugins, microservices, CQRS/MediatR, event bus, arbitrary PowerShell,
AI features, automatic remediation, directory writes and destructive legacy
data migrations.

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

## Autonomous execution rules

- Make normal technical decisions independently when existing code, tests,
  ADRs and roadmap boundaries provide a safe answer.
- Block only for a new product decision, personal-data expansion, credential
  policy change, external-system addition, directory/remote write, destructive
  migration or broad architectural conflict.
- Preserve unrelated changes. Stage named files only; never use blanket
  `git add -A`, destructive resets or cleanup against unverified paths.
- Commit small slices locally. Push and update a Draft PR only at large,
  fully verified phase milestones.
- Clean only temporary branches, worktrees and generated artifacts created by
  the roadmap run. Never delete user branches, user files or unrelated work.
- Existing configured AD, Kaspersky, opsi and Nessus systems may be queried
  only through bounded read-only paths. Until a remote test client is recorded
  in `ROADMAP_DECISIONS.md`, host-specific smoke tests are local-only.
- Passwords, tokens and credentials must never enter source, logs, snapshots,
  test output, commits or execution notes.
- A green PR may be merged when all merge gates pass. Tags, installer
  publication and GitHub Releases always require explicit user approval.
- GitHub Actions artifacts may be removed to restore quota; GitHub Release
  assets and published releases must not be altered by cleanup.

## Commands (once the skeleton exists)

```powershell
git diff --check
dotnet build --configuration Release
dotnet test --configuration Release
dotnet run --project tools/Wec.ContractGenerator --configuration Release -- --check

Set-Location frontend
npm ci
npm test -- --run
npm run build
npm audit --audit-level=high
```

Keep this file updated when foundational decisions change (new ADR ⇒ update summary here).
