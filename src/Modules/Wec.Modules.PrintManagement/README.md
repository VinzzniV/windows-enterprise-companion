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
"Daten vom …". Volatile values (status, toner, page count) are *not* carried —
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
devices and network connections — for the client-detail workspace. The Print
Management page merges queues that share a device (serial → IP → base name)
into one row in the UI; the serial-based lease diff is unaffected.

## Bridge actions

| Action | Payload | Result |
|---|---|---|
| `printmanagement/scanServer` | `{ target?: TargetRequest }` | `PrintServerSnapshot` — captures and persists (history); the response adds last-known device data for unreachable queues |
| `printmanagement/scanClientPrinters` | `{ target?: TargetRequest }` | `ClientPrinterScan` — printers installed on a client (MSFT_Printer, no SNMP, not persisted) |
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
hand-built BER fixtures; persistence round-trips run real migrations.
