# Wec.Modules.Diagnostics

Read-only troubleshooting snapshot of the **local machine**. Diagnostics are
deliberately not remote-capable: the network probes (gateway, DNS, DC
reachability) measure *this* machine's connectivity — running them elsewhere
would answer a different question. Results are never persisted.

## Bridge actions

| Action | Payload | Result |
|---|---|---|
| `diagnostics/runDiagnostics` | `{}` | `DiagnosticRunResult` — categorized results |

## Diagnostics by category

| Category | DiagnosticId | What it checks |
|---|---|---|
| Network | `WEC-DIAG-NET-CONFIG` | Physical adapters (IP, gateway, DNS, MAC, link speed, DHCP/static, type); virtual/filter adapters in a secondary result |
| Network | `WEC-DIAG-NET-GATEWAY` | Default gateway ping |
| DNS | `WEC-DIAG-NET-DNS` | Resolution of the configured probe hostname |
| DNS | `WEC-DIAG-DNS-SERVERS` | Ping of every configured DNS server (warning-only — ICMP is often filtered) |
| Domain | `WEC-DIAG-SYS-DOMAIN` | Domain/workgroup membership |
| Domain | `WEC-DIAG-DOM-DCREACH` | DC discovery via the domain's A records + ping (skipped on workgroup machines) |
| Time | `WEC-DIAG-SYS-TIMESYNC` | W32Time sync type, NTP server, service state |
| Services | `WEC-DIAG-SYS-SERVICES` | Monitored services running |
| Event logs | `WEC-DIAG-SYS-EVENTLOG` | Critical/error volume in the configured logs |
| System | `WEC-DIAG-SYS-DISKSPACE` | Free space on fixed drives |
| System | `WEC-DIAG-SYS-REBOOT` | Pending reboot (CBS, Windows Update, pending file renames) |
| System | `WEC-DIAG-SYS-UPDATES` | Days since the last installed update |

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
