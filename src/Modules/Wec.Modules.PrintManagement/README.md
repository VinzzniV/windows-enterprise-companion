# Wec.Modules.PrintManagement

Read-only printer inventory and lease tracking (ADR 0009). Two sources per
scan: the **print server** (queues, shares, drivers + versions, ports with
device IPs, location/comment) over CIM `root\StandardCimv2`
(`MSFT_Printer` / `MSFT_PrinterDriver` / `MSFT_PrinterPort`, local or
remote via the existing WMI seam), and the **devices themselves** over
**SNMP v2c read-only** (serial number, model, sysName/sysLocation,
hrPrinterStatus, page count, RFC 3805 supplies). The SNMP client is a
minimal own v2c GET/GETNEXT implementation behind the `ISnmpReader` Core
seam — no library, no write operation (ADR 0009).

Devices that do not answer become **per-printer errors** (typed code,
CIM data stays visible), never a scan abort. Ports without an IP
(WSD/local) stay CIM-only rows.

Such a queue keeps the **identity of its last successful scan** (serial, model,
sysName, sysLocation) so one unreachable printer does not blank out its row or
the CSV export; the entry carries `DeviceDataFromUtc` and the UI labels it
"Data from …". Volatile values (status, toner, page count) are *not* carried —
a stale toner level would be a lie. This happens on read (`scanServer` and
`getLatest` responses, `LastKnownDevices`), never in the stored snapshot:
snapshots stay pure measurements, otherwise the serial-based lease diff would
never report a device as gone and the consistency hints would go quiet.

A snapshot also lists **unused TCP/IP ports** — ports with a host address that
no queue references, i.e. the deletable leftovers after a printer is removed
(pseudo-ports like FILE:/LPT1:/nul: are excluded, they carry no address).

**The one write in an otherwise read-only module:** `deleteUnusedPorts` runs
`Remove-PrinterPort` on the server over WinRM (as the signed-in admin, via
`IPrinterPortRemover`), only after an explicit UI confirmation, and audits every
attempt to the log. The server refuses a port still bound to a queue, which is
the real safety net behind the "unused" classification. Credentials and port
names travel over stdin (names bound to `-Name` server-side), never on a command
line — no injection. No ADR (waived); the app stays unelevated (`asInvoker`).

Server vs. client is a deliberate split (ADR 0010): `scanServer` captures a
**print server** (queues + SNMP devices, above). `scanClientPrinters` captures
the printers **installed on a client** — `MSFT_Printer` only, no SNMP, local
devices and network connections — for the client-detail workspace. The client
scan uses the provider's `Type` mapping (`0` local, `1` connection) and falls
back to a UNC-name check only when that value is absent or unknown. Its latest
result is stored per client without retaining server-style history. The Print
Management page merges queues that share a device (serial → IP → base name)
into one row in the UI; the serial-based lease diff is unaffected.

The frontend boundary keeps ownership explicit: `PrintManagementPage` owns
network-policy and hint data, inventory composition and controlled
search/group/sort/expanded state. `usePrintServerWorkspace` owns initial
server/snapshot restore, scan state and bounded scan requests, add/remove target
persistence, derived server lists and the post-restore/post-scan hint-refresh
callback. It receives the page's target-request factory and optional target
save/delete functions. The read-only `LeaseSwapHistoryCard` owns its
server/baseline selection, History/Diff bridge requests and New/Gone/Swapped
result presentation; the page supplies only the currently available servers.
The side-effect-free `printerInventoryView` derives the
filtered, sorted and grouped physical-device rows plus the existing inventory
metrics from merged printers and reference data. `PrinterInventoryTable` is a
side-effect-free presentation component for group headings, sortable headers,
physical-device rows and the queue/toner/notification disclosure.
`PrintCsvExportCard` owns the CSV column schema and picker, projection of the
already filtered physical-device rows, `exportCsv` request and the
success/cancel/error result. The page retains only the header disclosure
trigger and supplies its current displayed devices; the host still owns the
save dialog and file write.
`UnusedPrinterPortsCard` owns unused-port selection, deduplicated reachability
checks, destructive confirmation, server-grouped deletion, outcome aggregation
and the disclosure table. The page supplies only the derived unused ports, the
target-request factory and the post-delete server-rescan callback. The backend
still enforces the safety boundary by refusing to remove ports that remain in
use.
`PrintServerManagerCard` is the controlled, side-effect-free presentation owner
for the add form, managed-server empty/list states, snapshot metadata, semantic
scan status, error guidance and Add/Rescan/Retry/Remove controls. Its feature
hook supplies the corresponding state and workflows; the page only binds the
returned values and callbacks.
`usePrinterNotificationChecks` owns the session-only CCRX password, checking
state, address-keyed results and bounded parallel `checkNotificationConfig`
requests. The page binds the existing controls to the hook and passes its result
map unchanged to the inventory view and table; per-device provider failures do
not discard successful sibling results.
`usePrinterDhcpCheck` owns the DHCP server value and configured default,
validation, request lifecycle and the atomic server/checked-IP/reservation
result. It receives only the network-policy default and target-request factory;
the page binds the existing controls and passes checked IPs and reservations
unchanged to the inventory view and table.
`PrintStatusView` supplies the small shared status renderer used by that table
and other Print workflow surfaces. The pure view/table boundaries receive
prepared data, reference data and callbacks; neither invokes the bridge nor
owns provider state.

The routed Print Management workspace follows the English product-language
contract for its operator guidance, validation, network/DHCP labels, CSV panel
and unused-port workflow. Printer names, locations, raw provider statuses and
consistency-hint content remain source data rather than translated UI copy.

Presentation semantics are intentionally separate from the bridge contracts.
Device observations map `Idle` to Execution `Idle`, `Printing`/`Warmup` to
Execution `Running`, and missing, failed or unknown observations to Availability
`Unknown`. Target, legacy, foreign and unclassified networks map to Lifecycle
`Current`, Lifecycle `Pending`, Health `Warning` and Availability `Unknown`.
DHCP reservation checks use Availability `Available`/`Missing` only for the
exact IPs included in a completed request. The frontend holds the queried DHCP
server, checked-IP set and returned reservations as one result; unchecked rows
make no reservation claim, while the KPI and local summary expose checked-scope
coverage. A new request, request failure or DHCP-server edit invalidates that
result. Notification checks use Health `Healthy`/`Warning` or Availability
`Unknown`; an active server scan uses Execution `Running`; port reachability
uses Availability `Available`/`Unknown`. The concrete context remains visible
and raw provider or error values remain technical details. Grouping the device table by status uses
the same canonical primary labels: provider `Printing`/`Warmup` observations
join `Running`, and missing, failed or unrecognized observations join
`Unknown`; their concrete contexts remain in the rows. No scan, SNMP, DHCP,
notification, ping, persistence or bridge value is changed by this presentation
mapping.

## Bridge actions

| Action | Payload | Result |
|---|---|---|
| `printmanagement/scanServer` | `{ target?: TargetRequest }` | `PrintServerSnapshot` — captures and persists (history); the response adds last-known device data for unreachable queues |
| `printmanagement/scanClientPrinters` | `{ target?: TargetRequest }` | `ClientPrinterScan` — printers installed on a client (`MSFT_Printer`, no SNMP); latest result persisted per client, no history |
| `printmanagement/listServers` | `{}` | stored servers with latest timestamp + snapshot count |
| `printmanagement/getLatest` | `{ server }` | latest stored snapshot (restore-on-load), same last-known enrichment |
| `printmanagement/getHistory` | `{ server }` | snapshot stamps, newest first |
| `printmanagement/getDiff` | `{ server, baselineSnapshotId? }` | `PrintServerDiff` — serial-based new/gone/swapped; default baseline = previous scan |
| `printmanagement/deleteServer` | `{ server }` | removes all snapshots of one server |
| `printmanagement/getHints` | `{}` | consistency hints across all stored latest snapshots |
| `printmanagement/exportCsv` | `{ csv }` | writes the CSV composed by the UI (chosen columns, one row per merged device) through the save dialog, UTF-8 with BOM |
| `printmanagement/openDeviceWebUi` | `{ address }` | opens `https://<address>/` in the default browser (host-name validated) |
| `printmanagement/deleteUnusedPorts` | `{ target, portNames, confirmed }` | `{ results: PortRemovalResult[] }` — deletes unused ports on the server; requires `confirmed`, audited |

## Toner semantics (RFC 3805)

`prtMarkerSuppliesLevel` of -1/-2 (unknown) or -3 ("some remaining")
renders as a chip without a percentage; a percentage only exists when
level and max capacity are real values. `IsLow` is evaluated at capture
time against `LowTonerThresholdPercent` — changing the threshold affects
the next scan, not stored snapshots.

## Consistency hints

Orphaned queues (port has an IP, device did not answer), driver version
spread per driver name across servers, queues without location and
comment, and the default `public` read community.

## Options (`Wec:PrintManagement`)

| Option | Default | Purpose |
|---|---|---|
| `SnmpCommunity` | public | Read community — never logged, never persisted outside configuration |
| `SnmpPort` | 161 | Agent port |
| `SnmpTimeout` | 3 s | Per-device timeout (wrong community = silent drop = timeout; the error text says so) |
| `HistoryLimit` | 50 | Snapshots kept per server (lease diffs need history) |
| `LowTonerThresholdPercent` | 15 | Low-toner boundary, evaluated at capture |
| `IgnoredQueues` | Microsoft Print to PDF, Microsoft XPS Document Writer, PDFCreator | Software pseudo-printers, dropped at scan time (prefix match on queue *and* driver name) — they never reach snapshots, hints or export |

## Known limitations

- SNMP values are proven against fixtures (standard Printer MIB); firmware
  variance of the real Utax/Kyocera fleet is confirmed on first live scan.
- Per-queue defaults (duplex/color) are not captured —
  `MSFT_PrinterConfiguration` costs one method call per queue and no use
  case needs it yet (ADR 0009).
- The device web UI opens with `https://` only; devices with HTTP-only
  admin pages need the browser to accept the redirect (or manual entry).

## Tests

`tests/Wec.Modules.PrintManagement.Tests` — CIM/SNMP seams mocked
(capture merge, per-device errors, supplies parsing, driver-version
unpacking, lease diff, ignored pseudo-printers, hints). The SNMP codec/transport is
tested in `tests/Wec.Infrastructure.IntegrationTests/Snmp` against
hand-built BER fixtures; persistence round-trips run real migrations. Client
printer tests pin the supported `MSFT_Printer` projection and `Type` mapping;
`tests/Wec.Infrastructure.IntegrationTests/Wmi/ClientPrinterScanServiceLocalTests.cs`
also performs a real local CIM smoke test.
