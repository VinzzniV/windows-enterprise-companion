# Wec.Modules.Diagnostics

Read-only troubleshooting snapshot per computer. WMI-based checks run
locally or against remote targets (ADR 0007); the connectivity probes
(gateway, DNS, DC reachability, time sync, event logs) measure *this*
machine's perspective — for remote targets they return a visible
`NOT_RUN` / `UnsupportedRemoteOperation` result instead of silently running
against the wrong machine. Results are never persisted.

## Bridge actions

| Action | Payload | Result |
|---|---|---|
| `diagnostics/runDiagnostics` | `{ target?: TargetRequest }` | `DiagnosticRunResult` — categorized results; empty target = local machine |

## Diagnostics by category

| Category | DiagnosticId | What it checks | Remote |
|---|---|---|---|
| Network | `WEC-DIAG-NET-CONFIG` | Physical adapters (IP, gateway, DNS, MAC, link speed, DHCP/static, type); virtual/filter adapters in a secondary result | local perspective |
| Network | `WEC-DIAG-NET-GATEWAY` | Default gateway ping | local perspective |
| DNS | `WEC-DIAG-NET-DNS` | Resolution of the configured probe hostname | local perspective |
| DNS | `WEC-DIAG-DNS-SERVERS` | Ping of every configured DNS server (warning-only — ICMP is often filtered) | local perspective |
| Domain | `WEC-DIAG-SYS-DOMAIN` | Domain/workgroup membership | yes (WMI) |
| Domain | `WEC-DIAG-DOM-DCREACH` | DC discovery via the domain's A records + ping (skipped on workgroup machines) | local perspective |
| Time | `WEC-DIAG-SYS-TIMESYNC` | W32Time sync type, NTP server, service state | local perspective (registry) |
| Services | `WEC-DIAG-SYS-SERVICES` | Monitored services running | yes (WMI) |
| Event logs | `WEC-DIAG-SYS-EVENTLOG` | Critical/error volume in the configured logs | local (Win32_NTLogEvent too slow over WinRM) |
| System | `WEC-DIAG-SYS-DISKSPACE` | Free space on fixed drives (Win32_LogicalDisk) | yes (WMI) |
| System | `WEC-DIAG-SYS-REBOOT` | Pending reboot (CBS, Windows Update, pending file renames) | yes (StdRegProv) |
| System | `WEC-DIAG-SYS-UPDATES` | Days since the last installed update | yes (WMI) |

Statuses: `PASS`, `WARNING` (ran, negative), `FAIL` (broken), `NOT_RUN`
(could not read — carries the error). A crashing diagnostic becomes a
visible `FAIL` result; the run continues.

Known limitation: clock *offset* against the time source is not measured
(that needs an NTP client); the time diagnostic reports configuration and
service state.

## Options (`Wec:Diagnostics`)

| Option | Default | Purpose |
|---|---|---|
| `DnsProbeHostname` | cloudflare.com | DNS resolution probe target |
| `ProbeTimeout` | 3 s | Ping/DNS probe timeout |
| `EventLogNames` | ["System"] | Logs summarized |
| `EventLogLookback` | 24 h | Summary window |
| `EventLogMaxEntries` | 500 | Read cap per log |
| `EventLogErrorWarningThreshold` | 50 | Errors per window before WARNING |
| `MonitoredServices` | Dhcp, Dnscache, LanmanWorkstation, EventLog | Services expected to run |
| `MinimumFreeDiskSpacePercent` | 10 | Free-space threshold per drive |
| `MaxDaysSinceLastInstalledUpdate` | 60 | Update recency threshold |

## Tests

`tests/Wec.Modules.Diagnostics.Tests` — all seams mocked
(`INetworkInfoProvider`, `IPingProbe`, `IDnsResolver`, `IDriveInfoProvider`,
`IRegistryReader`, `IWmiQueryService`, `IEventLogReader`).
