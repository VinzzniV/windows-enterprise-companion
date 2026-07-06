# ADR 0009: Print Management Module and SNMP Device Access

- **Status:** Accepted
- **Date:** 2026-07-03
- **Deciders:** Vinz
- **Supersedes:** —

## Context

The environment runs one Windows print server per location and a
Utax (Kyocera platform) device fleet. Recurring needs: a report with
serial number / location / queue name / model, tracking the ongoing
lease-renewal device swaps, and toner levels visible per printer. WEC
gets a read-only **Print Management** module.

Two data sources exist:

1. **The print servers** know queues, shares, drivers (+ versions), ports
   (+ device IPs), location/comment fields. All of it is exposed through
   the `MSFT_Printer` / `MSFT_PrinterDriver` / `MSFT_PrinterPort` CIM
   classes in `root\StandardCimv2` — reachable locally and remotely
   through the existing `IWmiQueryService` seam (ADR 0007: WinRM,
   credentials, typed errors, multi-host parallelism all come for free).
2. **The devices themselves** know serial number, model, status, toner
   levels and page counters. The vendor-neutral way to read them is
   **SNMP**: RFC 1213 system group (`sysName`, `sysLocation`), Host
   Resources MIB (`hrDeviceDescr`, `hrPrinterStatus`) and Printer MIB
   RFC 3805 (`prtGeneralSerialNumber`, `prtMarkerSupplies*`,
   `prtMarkerLifeCount`). SNMPv1/v2c is enabled on the fleet (read
   community, write community empty), and the standard MIB is uniformly
   supported on the Kyocera platform.

Deliberately out of scope (decided with the user, recorded in TODO.md):
device mail-notification config checks (vendor web-UI settings, not in
the standard MIB), address-book synchronization (the durable fix is the
devices' LDAP address book against AD), SNMP writes of any kind, and
central toner-order notifications (backlog; would need SMTP + scheduling
+ its own ADR).

### Options considered for the SNMP client

| Option | Assessment |
|---|---|
| **Minimal own SNMP v2c client (GET + GETNEXT walk) in Infrastructure** | The module needs exactly two operations against a fixed, small OID set: multi-OID GET and a subtree walk of the supplies table. That is one BER encoder/decoder (~a few hundred lines of pure, unit-testable byte handling) plus a thin UDP transport. No new dependency, no version/maintenance surface, full control over timeouts and error mapping. |
| SNMP library (e.g. Lextm.SharpSnmpLib) | Solid, but pulls a general-purpose SNMP stack (v3, traps, agents, security models) for two read operations. Violates the "no dependency for what a few lines can do" rule; v3 support is not needed (fleet runs v2c and WEC only reads). Revisit if SNMPv3 ever becomes a requirement — that flips this decision. |
| Vendor fleet tools / IPP / web-UI scraping | Vendor-specific, fragile across firmware, or a heavier protocol client. The standard MIB answers everything the use cases need. |

## Decision

1. **New read-only module `Wec.Modules.PrintManagement`.** Print-server
   inventory over the existing `IWmiQueryService`
   (`root\StandardCimv2`: `MSFT_Printer`, `MSFT_PrinterDriver`,
   `MSFT_PrinterPort`); per-queue defaults from
   `MSFT_PrinterConfiguration` are skipped for now (one method call per
   queue — cost without a driving use case).
2. **Device access is SNMP v2c, read-only, behind a new Core seam
   `ISnmpReader`** (`GetManyAsync` for a fixed OID list, `WalkAsync`
   for the supplies subtree). The implementation is a **minimal own
   v2c client** in Infrastructure: a pure BER codec (unit-tested
   against byte fixtures) and a small UDP transport. No SNMP writes —
   the seam does not expose one.
3. **Community string handling:** the read community is configuration
   (`Wec:PrintManagement:SnmpCommunity`), passed per request in
   `SnmpEndpoint`, never logged and never persisted beyond
   configuration. A configured default community of `public` is
   surfaced as a consistency hint in the UI, not silently accepted.
4. **Unreachable devices are per-printer partial results**, never a scan
   abort: the queue row renders with a typed device error
   (`CONNECTION_TIMEOUT` / `SERVICE_UNAVAILABLE`) while CIM data stays
   visible. This mirrors the per-host error model of ADR 0007.
5. **Snapshots keep history** (unlike the hardware inventory's
   latest-only store): the lease renewal needs "new / gone / swapped at
   the same queue" diffs between points in time. Serial numbers are the
   diff key; retention is bounded by
   `Wec:PrintManagement:HistoryLimit` per server.
6. **The device web UI is opened via `IShellLauncher`** (default
   browser, `https://<port IP>`) — WEC does not embed foreign device
   pages in its WebView.

## Consequences

- The module stays inside the read-only guarantee; the only writes are
  WEC-internal (snapshot persistence).
- The own SNMP client means WEC owns ~one file of protocol code; the
  codec is pure and fixture-tested, the transport is trivial. If SNMPv3
  is ever required, switching to a library is an implementation swap
  behind `ISnmpReader` and an ADR revision.
- Toner semantics follow RFC 3805: `prtMarkerSuppliesLevel` of -1/-2
  (unknown) and -3 ("some remaining") render as unknown/OK instead of a
  fake percentage; percentages are only computed when level and max
  capacity are real values.
- SNMP runs against device IPs harvested from the print-server ports —
  printers reachable only via WSD or local USB have no IP and simply
  stay CIM-only rows (visible, not enriched).
- Live validation against the real Utax fleet is the known follow-up;
  the standard-MIB OIDs are fixture-tested but device firmware variance
  is only proven on first real scan (same situation as the opsi module).
