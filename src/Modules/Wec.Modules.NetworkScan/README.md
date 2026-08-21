# Wec.Modules.NetworkScan

Active network discovery for a user-supplied target (CIDR, octet range, single IP
or a space-separated list). Answers three questions per host:

1. **Is it alive, and what is it?** — nmap host discovery + a no-elevation connect
   scan of a small port set; device type (Printer / Computer / NetworkDevice /
   Unknown) is inferred from open ports, service banners and the MAC vendor.
2. **Does it have a DHCP reservation?** — optional, joined from the DHCP server via
   `IDhcpReader`. Live host without a reservation → **rogue**; reserved IP inside
   the scanned range that answered nothing → **stale** reservation.
3. **What is its reverse-DNS name?** — taken straight from nmap's own resolution.

## Presentation semantics

The routed result table maps the existing four reconciliation states to the
shared UI status contract without changing their backend values. A live host
with a reservation is Availability `Available` plus `Reserved`; a live host
without a reservation is Availability `Missing` plus `No reservation`; a
reserved address that did not answer is Freshness `Stale` plus `Reservation did
not answer`; and a live host without a DHCP check is Availability `Available`
plus `Active`. Device kind remains a separate heuristic classification, not a
Health or Availability status. Reservation names remain technical tooltip
details. Discovery, DHCP reconciliation, credentials, metrics and bridge
contracts are unchanged by this presentation mapping.

## Boundaries

- **Read-only.** nmap never writes; the DHCP path only reads reservations.
- **No elevation** (ADR 0002): a TCP connect scan (`-sT`) needs no raw sockets.
  If the local nmap/Npcap install still refuses to run unelevated, the user can
  use the app's "Restart as administrator" button.
- **No OS fingerprint** (`-O` needs raw packets/elevation), so device typing is a
  heuristic, not a promise. Ambiguous hosts stay `Unknown` rather than guess.
- The nmap target is validated and passed as separate process arguments (never a
  shell), so it cannot inject nmap flags or commands. DHCP credentials are the
  session admin identity, in-memory only, handed to the child over stdin
  (ADR 0007) — never persisted, never logged.

## Seams

- `INetworkScanner` (`Wec.Core.Network`) → `NmapScanner` (Infrastructure): runs
  nmap, parses `-oX` XML with DTD processing disabled (XXE-safe).
- `IDhcpReader` (`Wec.Core.Dhcp`) → `PowerShellDhcpReader`: reused as-is.

## Options (`Wec:NetworkScan`)

- `NmapPath` — override nmap.exe location; empty = default install path, then PATH.
- `ScanPorts` — TCP ports probed when a port scan is requested.
- `ScanTimeout` — hard ceiling for a single run.
