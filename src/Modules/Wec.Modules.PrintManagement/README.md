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

Server vs. client is a deliberate split (ADR 0010): `scanServer` captures a
**print server** (queues + SNMP devices, above). `scanClientPrinters` captures
the printers **installed on a client** — `MSFT_Printer` only, no SNMP, local
devices and network connections — for the client-detail workspace. The Print
Management page merges queues that share a device (serial → IP → base name)
into one row in the UI; the serial-based lease diff is unaffected.

## Bridge actions

| Action | Payload | Result |
|---|---|---|
| `printmanagement/scanServer` | `{ target?: TargetRequest }` | `PrintServerSnapshot` — captures and persists (history) |
| `printmanagement/scanClientPrinters` | `{ target?: TargetRequest }` | `ClientPrinterScan` — printers installed on a client (MSFT_Printer, no SNMP, not persisted) |
| `printmanagement/listServers` | `{}` | stored servers with latest timestamp + snapshot count |
| `printmanagement/getLatest` | `{ server }` | latest stored snapshot (restore-on-load) |
| `printmanagement/getHistory` | `{ server }` | snapshot stamps, newest first |
| `printmanagement/getDiff` | `{ server, baselineSnapshotId? }` | `PrintServerDiff` — serial-based new/gone/swapped; default baseline = previous scan |
| `printmanagement/deleteServer` | `{ server }` | removes all snapshots of one server |
| `printmanagement/getHints` | `{}` | consistency hints across all stored latest snapshots |
| `printmanagement/exportCsv` | `{ servers? }` | semicolon-separated CSV via the save dialog (serial, location, name, model, toner, IP, …) |
| `printmanagement/openDeviceWebUi` | `{ address }` | opens `https://<address>/` in the default browser (host-name validated) |

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
unpacking, lease diff, CSV escaping, hints). The SNMP codec/transport is
tested in `tests/Wec.Infrastructure.IntegrationTests/Snmp` against
hand-built BER fixtures; persistence round-trips run real migrations.
