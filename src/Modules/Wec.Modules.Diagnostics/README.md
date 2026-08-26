# Wec.Modules.Diagnostics

Read-only device-health snapshot per computer. The implementation keeps the
existing module, bridge actions and persistence names for compatibility while
the product surface is presented as **Health** (ADR 0018).

Health runs only on demand. WMI-based checks work locally and against remote
targets. The event-log summary is local-only because `Win32_NTLogEvent` is too
slow for a broad remote summary; the separate preset-based Event Log view
continues to query remote targets through WMI.

The latest run is persisted per host and restored when that client is opened.
Existing `diagnostics_runs` payloads remain readable, including results from
checks that are no longer executed.

## Bridge actions

| Action | Payload | Result |
|---|---|---|
| `diagnostics/runDiagnostics` | `{ target?: TargetRequest }` | `DiagnosticRunResult`; empty target = local machine |
| `diagnostics/getLatestDiagnostics` | `{ target?: TargetRequest }` | latest persisted `DiagnosticRunResult` for the host, or `null` |
| `diagnostics/queryEventLog` | `{ preset, target?: TargetRequest }` | live preset-based Event Log result |

The bridge names deliberately remain stable. A later contract migration may
introduce `health/*` aliases only when compatibility requires it.

## Active health checks

| Category | DiagnosticId | What it checks | Remote |
|---|---|---|---|
| Services | `WEC-DIAG-SYS-SERVICES` | Configured Windows service states | yes |
| Event logs | `WEC-DIAG-SYS-EVENTLOG` | Critical/error volume in configured logs | summary local-only |
| System | `WEC-DIAG-SYS-DISKSPACE` | Free space on fixed drives | yes |
| System | `WEC-DIAG-SYS-UPDATES` | Age of the most recently installed update | yes |

Network configuration, gateway/DNS/DC probes, domain membership, time
synchronization and pending-reboot checks are no longer executed. Network Scan
is an independent module and remains unchanged.

Statuses are `PASS`, `WARNING`, `FAIL` and `NOT_RUN`. A crashing check becomes
a visible `FAIL`; the remaining checks continue. Results are ordered
`FAIL -> WARNING -> NOT_RUN -> PASS`.

## Options (`Wec:Diagnostics`)

| Option | Default | Purpose |
|---|---|---|
| `EventLogNames` | ["System"] | Logs summarized locally |
| `EventLogLookback` | 24 h | Summary window |
| `EventLogMaxEntries` | 500 | Read cap per log |
| `EventLogErrorWarningThreshold` | 50 | Errors per window before warning |
| `MonitoredServices` | Dhcp, Dnscache, LanmanWorkstation, EventLog | Services expected to run |
| `MinimumFreeDiskSpacePercent` | 10 | Free-space threshold per drive |
| `MaxDaysSinceLastInstalledUpdate` | 60 | Update-recency threshold |

## Tests

`tests/Wec.Modules.Diagnostics.Tests` characterizes all four active checks, the
run ordering/failure behavior, persistence and the preset-based Event Log query.
