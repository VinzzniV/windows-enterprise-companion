# Shared UI design system

Dark-first, data-dense enterprise look. Everything here is used across the
feature pages — build pages from these, don't re-style ad-hoc.

## Tokens (`frontend/src/index.css`, Tailwind v4 `@theme`)

- **Fonts** (bundled via `@fontsource-variable`, no CDN — ADR 0001):
  `--font-sans` = Inter (all UI), `--font-mono` = JetBrains Mono (identifiers:
  serials, IPs, MACs, OIDs, versions). Use `font-mono tabular-nums` for those.
- **Accent** (`--color-accent-*`, indigo): primary buttons, active nav,
  links, focus ring, selection. Matches the LogoMark gradient.
- **Status** (`--color-{ok,warn,fail,info}-*`): the only semantic status
  colors. `ok` = healthy, `warn` = attention, `fail` = broken, `info` =
  notable. Never use raw `emerald/amber/red/sky` utilities on pages — use the
  token names so the palette stays cohesive.

## Primitives

| Component | Use |
|---|---|
| `Button` (`primary`/`secondary`/`ghost`) | Actions; `primary` = the page's main action |
| `Input`, `Select`, `Checkbox`, `Field`, `Toolbar` | All form controls — never hand-roll an input's classes; `controlClass` is the shared base |
| `Badge` (`ok/warn/fail/info/neutral/accent`) | Status as **dot + label** (color is never the only signal); `StatusBadge`/`SeverityBadge` map onto it |
| `SummaryMetric` | Number-over-label tile; tones map to the status semantics above |
| `DataTable` | Dense table; per-column `align` + `mono`, `zebra` (default), `stickyHeader`, accessible `onRowClick` |
| `Card`, `PageHeader`, `DetailsDisclosure`, `EvidenceList`, `Spinner` | Structure and disclosure |
| `EmptyState`, `ErrorState` | The only empty/error presentation — no bespoke alert divs |
| `LogoMark`, `ErrorBoundary` | Branding, per-route error isolation |

## Conventions

- One result-context line per result view (host · status · timestamp · counts).
- Multi-host scans use the shared `TargetSelector` (Local / Remote / Multiple
  + AD computer picker) and the `runWithConcurrencyLimit` pool.
- Every icon-only control has an `aria-label`; focus is visible everywhere
  (`:focus-visible` accent ring); `prefers-reduced-motion` is respected.
- `api-types.ts` is hand-mirrored from the C# DTOs — update it on every DTO
  change.

Master/detail is now the **Clients** workspace (ADR 0010):
`features/clients` lists AD-sourced clients and opens a per-client detail whose
Inventory/Security/Diagnostics/Printers sections reuse the exported feature
views (`SnapshotGrid`, `FindingCard`/`CoverageNotes`, `RunSummary`/
`CategorySections`) and scan on demand through the shared `TargetProvider`
(`shared/targets/TargetContext`) — enter credentials once per host per session.
Saved targets (`SavedTargetsBar`, backed by `Wec.Modules.Targets`) pre-fill
pickers by role. Print Management keeps a single consolidated table but merges
queues per physical device with search and site grouping; the standalone
Inventory/Security/Diagnostics pages remain as Fleet multi-host batch runners.
