# Shared UI design system

Dark-first, data-dense enterprise look. Everything here is used across the
feature pages — build pages from these, don't re-style ad-hoc.

## Tokens (`frontend/src/index.css`, Tailwind v4 `@theme`)

- **Fonts** (bundled via `@fontsource-variable`, no CDN — ADR 0001):
  `--font-sans` = Inter (all UI), `--font-mono` = JetBrains Mono (identifiers:
  serials, IPs, MACs, OIDs, versions). Use `font-mono tabular-nums` for those.
- **Accent** (`--color-accent-*`, indigo): primary buttons, active nav,
  links, focus ring, selection. Matches the LogoMark gradient.
- **Muted content** (`--color-muted` = `#94a3b8`, Slate 400): small helper,
  metadata, table-header, placeholder and subdued context text on the Slate 900
  and Slate 950 product surfaces. It retains at least WCAG AA 4.5:1 contrast on
  both backgrounds. Use direct Slate 500 text only for disabled or decorative
  elements that do not carry information by themselves; the contrast regression
  in `contrast.test.ts` guards the semantic token.
- **Status** (`--color-{ok,warn,fail,info}-*`): the only semantic status
  colors. `ok` = healthy, `warn` = attention, `fail` = broken, `info` =
  notable. Never use raw `emerald/amber/red/sky` utilities on pages — use the
  token names so the palette stays cohesive.

## Primitives

| Component | Use |
|---|---|
| `Button` (`primary`/`secondary`/`ghost`/`danger`) | Actions; `primary` = the page's main action, `danger` = a destructive persisted-state action |
| `ConfirmDangerAction` | Two-step confirmation for destructive persisted-secret actions: use a danger trigger, name the concrete consequence, require an explicit final confirmation, and put initial focus on Cancel |
| `Input`, `Select`, `Checkbox`, `Field`, `Toolbar` | All form controls — never hand-roll an input's classes; `controlClass` is the shared base |
| `Badge` (`ok/warn/fail/info/neutral/accent`) | Low-level dot + label primitive; color is never the only signal |
| `SemanticStatusBadge` | Canonical WEC-owned Health, Execution, Availability, Lifecycle and Freshness status; the typed dimension determines both label and tone |
| `StatusBadge`, `SeverityBadge` | Existing feature/provider mappings that have not yet moved to the canonical multidimensional contract |
| `SummaryMetric` | Number-over-label tile; tones map to the status semantics above. Optional `onClick`/`active` props render a keyboard-focusable filter button with `aria-pressed`; provide a task-specific `ariaLabel`. |
| `DataTable` | Dense table; local sort for small datasets or controlled sort/pagination/loading for feature-owned server queries; per-column `align` + `mono`, `zebra` (default), `stickyHeader` |
| `DetailDialog` | Viewport-bound detail workflow for long lists; traps focus, closes with Escape and returns focus to the triggering row or control |
| `Card`, `PageHeader`, `DetailsDisclosure`, `EvidenceList`, `Spinner` | Structure and disclosure |
| `EmptyState`, `ErrorState` | The only empty/error presentation — no bespoke alert divs |
| `LogoMark`, `ErrorBoundary` | Branding, per-route error isolation |

## Conventions

Bridge failures use `shared/bridge/errorPresentation.ts`. Keep the administrator-facing
message, probable cause and next action visible; pass original codes, backend messages,
details and required privileges to `ErrorState.technicalDetails`, which is collapsed by
default. Feature code may specialize the main message through `presentError` context but
must not rebuild `CODE: message details` strings. Place retry or recovery controls in
`ErrorState.controls` so the action stays attached to the affected operation.

- One result-context line per result view (host · status · timestamp · counts).
- Multi-host Inventory, Security and Health scans use explicit bounded selection
  in the canonical Clients workspace. Selecting a row never starts a scan.
- Every icon-only control has an `aria-label`; focus is visible everywhere
  (`:focus-visible` accent ring); `prefers-reduced-motion` is respected.
- `api-types.generated.ts` is generated from the C# bridge DTOs;
  `api-types.ts` only retains intentional UI aliases and event documentation.

## Semantic status contract

Use `SemanticStatusBadge` for WEC-owned states after the feature's domain value
has been mapped explicitly to one of these dimensions:

| Dimension | Canonical values |
|---|---|
| Health | `Healthy`, `Warning`, `Critical` |
| Execution | `Idle`, `Running`, `Succeeded`, `Partial`, `Failed` |
| Availability | `Available`, `Missing`, `Unknown`, `Not configured`, `Not applicable` |
| Lifecycle | `Current`, `Update available`, `Pending`, `Disabled` |
| Freshness | `Fresh`, `Stale` |

Freshness is intentionally independent: stale data can still be available and
must never be presented as missing or unknown. `ok` is reserved for healthy,
successful, available, current or fresh; `warn` means attention, partial work,
missing data, pending lifecycle or stale data; `fail` means critical health or
failed execution; `info` means running; neutral means idle, unknown, not
configured, not applicable or disabled. Preserve raw provider and protocol
values until a feature-specific mapping has been verified instead of guessing
their semantic dimension.

Dashboard data states are mapped across Execution, Freshness and Availability.
Report source readiness maps backend `READY`, `MISSING`, `STALE` and
`INCOMPLETE` to `Fresh`, `Missing`, `Stale` and `Partial`; an unrecognized
contract value is displayed as neutral `Unknown`. This presentation mapping
does not change the raw readiness values retained by backend or export
contracts. The aggregate readiness maps an all-sources-ready result to
Availability `Available` and every not-fully-ready source composition to
Execution `Partial`; the adjacent summary and individual source rows retain
the exact reason. Export availability and backend readiness evaluation remain
unchanged.

Diagnostic results map persisted `PASS`, `WARNING`, `FAIL` and `NOT_RUN` to
Health `Healthy`, Health `Warning`, Health `Critical` and Availability
`Unknown`. The shared summary and result rows use the same mapping in both the
standalone view and the Clients workspace; persisted and bridge values remain
unchanged.

The WEC-owned Nessus synchronization summary maps an active operation to
Execution `Running`, idle to `Idle`, a clean completion to `Succeeded`, a
completion carrying a persisted error to `Partial`, and failure to `Failed`.
A non-running intermediate phase falls back to Availability `Unknown`. The
specific active phase and scan count remain adjacent context instead of being
used as the status badge; raw bridge values and synchronization behavior are
unchanged.

Individual Nessus scan rows use a separate presentation mapping for all 15
[Tenable-documented scan statuses](https://developer.tenable.com/docs/scan-status-tio).
They distinguish `Succeeded`, `Running`, `Pending`, `Missing`, `Available`,
`Failed`, `Partial` and `Unknown` while keeping transition or outcome context
such as `Initializing`, `Paused`, `Never run` or `Canceled` adjacent to the
canonical badge. Provider status and per-scan error text remain available only
as technical descriptions; API, persistence and synchronization contracts are
unchanged.

Security result coverage maps a known complete evaluation to Availability
`Available`, a known incomplete evaluation to Execution `Partial`, and legacy
or otherwise unknown coverage to Availability `Unknown`. The concrete
`Coverage complete`, `Coverage incomplete` or `Coverage unavailable` wording
stays adjacent to the canonical badge. Scan process status, per-check outcomes,
finding severity and persisted coverage values remain separate and unchanged.

Environment inventory sources share one presentation mapping across load
progress, Fleet posture and the client Overview: `AVAILABLE` maps to
Availability `Available`, `RUNNING` to Execution `Running`, `PARTIAL` and
`TRUNCATED` to Execution `Partial`, `NOT_CONNECTED` to Availability
`Not configured`, and `UNAVAILABLE` to Execution `Failed`. Distinguishing
details such as `Not connected`, `Source unavailable` and `Result truncated`
remain adjacent context. This mapping does not change bridge values, source
collection, caching, device-presence semantics or hygiene assessment.

The Clients workspace maps the hygiene assessment separately from source
state: healthy and warning assessments are Health `Healthy` and `Warning`;
critical and cleanup-candidate assessments are Health `Critical`, with
`Cleanup candidate` retained as context. Incomplete assessment is Execution
`Partial`; an unmanaged row is Availability `Unknown`. Source presence is
Availability `Available`, `Missing` or `Not applicable`, while a present
source uses Freshness `Fresh`, `Stale` or Availability `Unknown` when its
timestamp is absent. Disabled AD objects and outdated Kaspersky components are
Lifecycle `Disabled` and `Update available`. Compare-picker Inventory and
Security snapshots use Availability `Available` or `Missing`. These mappings
do not alter hygiene findings, thresholds, risk severity, timestamps or stored
snapshot contracts.

Privileged Active Directory member pages treat account state as a separate
lifecycle family. A confirmed enabled User or Computer is Lifecycle `Current`
with `Account enabled` context, a disabled account is Lifecycle `Disabled`, a
missing `userAccountControl` value is Availability `Unknown`, and directory
objects without an account lifecycle are Availability `Not applicable`.
Unknown future bridge values also fall back to `Unknown`; raw values remain in
technical descriptions and identity details. LDAP derivation, search, sorting
and paging are unchanged.

The manual visible-page connectivity check is independent from inventory and
assessment. Each requested row replaces any previous result with Execution
`Running`; a request failure becomes Execution `Failed` with an actionable
local retry. A confirmed ICMP reply or open WinRM port 5985 is Availability
`Available`, with the exact responding channels adjacent. When neither channel
answers, or the response omits one requested host, the row is Availability
`Unknown` rather than claiming that the device is offline. Completed and
failed rows show their local check time. The ICMP/TCP implementation,
visible-page request boundary and bridge payload remain unchanged.

Print Management maps the device observation independently from network and
workflow state. SNMP `Idle` is Execution `Idle`; `Printing` and `Warmup` are
Execution `Running`; missing, failed or unrecognized observations are
Availability `Unknown`. Network classification uses Lifecycle `Current` or
`Pending`, Health `Warning` for a foreign VLAN, and Availability `Unknown` when
unclassified. DHCP reservations use Availability `Available` or `Missing`, but
only for an IP included in the completed request. The result atomically keeps
the queried DHCP server, exact checked-IP set and returned reservations; rows
outside that set show no reservation claim, and the summary exposes checked-IP
coverage. Starting or failing another request, or editing the DHCP server,
discards the prior result. Notification checks use Health `Healthy` or
`Warning`, with Availability `Unknown` before or after an indeterminate check;
a server scan is Execution `Running`; port reachability is Availability
`Available` or `Unknown`. Visible context explains the concrete state, while raw
SNMP/provider/error values stay in technical details. The optional status grouping uses the same canonical
primary labels, so `Printing` and `Warmup` share `Running`, while absent,
failed and unrecognized observations share `Unknown`; row context continues to
explain the concrete cause. These mappings do not change scan, DHCP,
notification, ping, persistence or bridge behavior.

Network Scan keeps host availability and DHCP reconciliation explicit. A live
reserved row is Availability `Available` with `Reserved` context; a live row
without a reservation is Availability `Missing` with `No reservation`; a
reserved address that did not answer is Freshness `Stale` with `Reservation did
not answer`; and a live row without a DHCP check is Availability `Available`
with `Active`. Device kind remains a separate heuristic classification rather
than a semantic status. These mappings do not change nmap discovery, DHCP
reconciliation, credentials, metrics or bridge values.

Patch Management maps the aggregate package status independently from the
client workflow state. `CURRENT` and `UPDATE_AVAILABLE` are Lifecycle `Current`
and `Update available`; `DEPOT_DEVIATION` is Health `Warning` with `Depot
deviation`; `MISSING_ON_DEPOT` is Availability `Missing` with `From depot`;
`CHECK_FAILED` is Execution `Failed` with `Package check`; and
`ACTION_PENDING` is Lifecycle `Pending` with `opsi action pending`. An unrecognized
value is Availability `Unknown` with `Package status unavailable`, while its raw
value remains technical detail. Package derivation, filtering, workflow states
and bridge values are unchanged.

The client patch view uses the same dimensions for read-only opsi state.
`COMPLETED` is Lifecycle `Current`, `UPDATE_AVAILABLE` is Lifecycle
`Update available`, `ACTION_PENDING` is shown neutrally as an existing opsi
action, and `FAILED` is Execution `Failed`. WEC does not create or change these
client states. Winget catalog failures are separate from opsi availability and
therefore never replace a usable depot/client overview.

Patch audit results are a fifth independent family. `SUCCESS` and `FAILED` map
to Execution `Succeeded` and `Failed`. `PLANNED` records the successful
creation of a read-only package preview, so it is Execution `Succeeded` with
visible `Preview created` context rather than a pending lifecycle. Unknown
future values fall back to Availability `Unknown` with `Audit result
unavailable`; the raw value remains technical detail. Persisted result values,
actions, ordering and audit behavior are unchanged.

The Patch header's opsi connection check is a sixth independent family. Before
the read-only check resolves it is Execution `Running`; a transport or
automatic-connection failure is Execution `Failed`. A confirmed live session
is Availability `Available`, while a successfully verified response without a
session is Availability `Unknown` with `Not connected` context. The header does
not claim a disconnected end state while the check is still pending. Server,
account and opsi version remain adjacent context; session, credential and
bridge behavior are unchanged.

## Product language

English is the administrator-facing product language for the shell, routed
workspaces, controls, status labels, validation, empty states and recovery
guidance. Do not mix languages within a workflow. Preserve protocol values,
vendor product names, host-provided evidence and original provider messages.
Provider-owned entity names remain data in their normal tables; raw messages,
codes and other evidence that need explanation belong in collapsed technical
details with English operator guidance. The audited Shell, Network Scan, Patch
Management, Print Management and Settings-owned hint migration is complete
without a localization layer. Settings examples are English, while configured
provider entity names such as KSC administration groups remain verbatim.
The multidimensional status contract is established and used by Dashboard data
states, report aggregate and source readiness, diagnostic results, Nessus
synchronization and individual scan states, Security result coverage,
environment-source progress and the Clients
assessment/source/snapshot surfaces as well as Print device, network, DHCP,
notification, scan and reachability states plus Network Scan availability and
DHCP reconciliation as well as Patch package, client-workflow,
manufacturer-source, approval-chain, audit-result and connection-check states.
F-17 is complete for the routed product surface. Non-routed legacy standalone
pages remain tracked separately as roadmap item N-05 and must satisfy this
contract before any future route reactivation.

## Application shell

`app/App.tsx` owns the responsive shell. At `xl` and above it renders the
fixed 224-pixel desktop navigation. Below `xl`, navigation is removed from the
layout and exists only while the labelled menu button opens the modal drawer.
The drawer closes through its close control, Escape or route selection, makes
the background inert and returns focus to the menu trigger. Its navigation
area scrolls independently inside the transient drawer so Administration and
the runtime footer remain reachable at 800 × 600.

The route content is the only persistent vertical scroll container. Shell
padding scales from `p-3` through `p-4` to `p-6`; feature tables and tab strips
own any necessary horizontal scrolling instead of widening the shell.

Master/detail is now the **Clients** workspace (ADR 0010):
`features/clients` lists the de-duplicated environment, scan-history and saved-
target inventory and opens a per-client detail whose
Inventory/Security/Diagnostics/Printers sections reuse the exported feature
views (`SnapshotGrid`, `FindingCard`/`CoverageNotes`, `RunSummary`/
`CategorySections`) and scan on demand through the shared `TargetProvider`
(`shared/targets/TargetContext`) — enter credentials once per host per session.
Saved targets (`SavedTargetsBar`, backed by `Wec.Modules.Targets`) pre-fill
pickers by role. Print Management keeps a single consolidated table but merges
queues per physical device with search and site grouping. Its page owns data,
network-policy/hint requests, inventory composition and controlled view state.
The feature-local `usePrintServerWorkspace` hook owns stored-snapshot restore,
scan state and bounded scan requests, add/remove target persistence, derived
server lists and the post-restore/post-scan hint-refresh callback. The
read-only `LeaseSwapHistoryCard` owns the self-contained History/Diff request
and selection/result state while receiving only the available server list. The
side-effect-free `printerInventoryView` projects the merged fleet into filtered, sorted and
grouped rows plus the existing inventory metrics; `PrinterInventoryTable` owns
only the grouped table, sortable headers, compact rows and
queue/toner/notification disclosure. `PrintCsvExportCard` owns the CSV schema,
column selection, projection of the currently displayed physical devices,
export request and result while the page retains only its header trigger. The
`UnusedPrinterPortsCard` owns port selection, deduplicated reachability,
confirmed server-grouped deletion, aggregated outcomes and the unused-port
table while receiving derived ports, a target factory and a rescan callback
from the page. `PrintServerManagerCard` is the controlled, side-effect-free
presentation owner for the add form, managed-server states, snapshot metadata,
scan/error presentation and Add/Rescan/Retry/Remove controls; its feature hook
supplies all associated state and workflows while the page binds values and
callbacks.
While stored print-server snapshots are restoring, the card exposes a neutral
loading state and withholds first-run and add controls. Once restore settles,
an empty server list gets the single guided hostname/Add & scan entry; a
managed fleet without captured queues gets one page-level Rescan-oriented
empty state.

`usePrinterNotificationChecks` owns the session-only CCRX
password, checking lifecycle, address-keyed results and bounded parallel
provider requests while the page retains the existing controls.
`usePrinterDhcpCheck` owns DHCP server/default state, validation, request
lifecycle and the atomic checked-IP/reservation result while the page retains
the existing controls and result text. The pure
view/table boundaries receive data and callbacks without invoking the
bridge or owning provider state; the export workflow is the intentional
feature-local bridge owner for the host save dialog and the port component is
the intentional feature-local bridge/workflow owner for reachability and safe
port deletion. The notification hook is the corresponding feature-local data
and bridge owner for per-device notification checks; the DHCP hook is the
corresponding owner for scoped reservation checks.
Patch Management keeps connection/dashboard lifecycle, credential-free cache
persistence, depot/default resolution, due Winget checks and live/stale state in
`usePatchManagementWorkspace`. `PatchProductOverviewWorkspace` owns the local
package filter, KPI summary and read-only opsi depot table. `WingetPackagesWorkspace`
owns catalog search, eligibility display, editable Product-ID/depot selection,
creation/adoption preview, explicit confirmation, managed-package selection and
batch update preview. A daily cached check and a forced check share the same
backend action. Build buttons remain disabled during an operation, and every
write displays a fresh server preview before sending `confirmed: true`.
`PatchClientFleetCard` owns the read-only paged client/package-state query. The
read-only
`PatchAuditHistoryCard` owns the audit-log query, entry/error state, local retry,
semantic result badge and table; it reads the current persisted history only
when its exclusive tab mounts, while administrative actions remain independent
of the hidden view. No frontend path exposes mapping, manufacturer scraping,
pilot, synchronization or client-action requests.
Inventory, Security and Diagnostics are currently embedded in Clients rather
than routed as standalone pages.

Client sections are URL-addressable through the allowlisted `section` query
parameter (for example, `/clients/HOST?section=inventory`). Overview is the
canonical default without a query parameter; unknown values fall back safely
to Overview. Report-readiness cards use these links for non-ready Inventory and
Security sources, but navigating to a section never starts a scan automatically.

Settings uses its own feature-local, sticky section navigation. Effective
Configuration is the parameterless default; the allowlisted `section` values
`environment-health`, `vulnerability-management`, `patch-management` and
`policy` address the remaining cards. Unknown values are removed. Following a
section link only updates the URL and scroll position; it must never save
settings, connect a provider or trigger a credential action.

Environment Health, Vulnerability Management and Patch Management each compare
their editable settings with a separate last-loaded-or-successfully-saved
baseline. A dirty section is labelled `Unsaved changes` both in the local
navigation and beside its Save action; color is supplementary only. A failed
save must preserve that state, and a successful save may clear only its own
section. Credential and connection drafts remain governed by their dedicated
explicit actions rather than these settings baselines.

The three settings save boundaries mirror their existing backend validation
rules in feature-local pure validators. Current problems appear in one
accessible summary below the section navigation; every issue links to its
canonical section, and each navigation item shows its own validation count.
Only the invalid section's Save action is disabled, while unrelated sections
remain operable. Backend validation remains mandatory as the second line of
defense; the client summary does not replace it.

The Clients master is also the canonical **Fleet posture** view. Its source
availability, assessment time, summary metrics and table rows come from one
request-bound hygiene snapshot; summary counts are recalculated after the same
client-identity de-duplication used by the table, so KPI and result totals stay
aligned. KPI and select drill-downs share the allowlisted
`posture` query parameter (for example,
`/clients?posture=MISSING_KASPERSKY`); unknown values fall back to `ALL`.
`/employeelifecycle?filter=<FILTER>` is compatibility-only and redirects
allowlisted values into this route. Do not add a second hygiene device table.
