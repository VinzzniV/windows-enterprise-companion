# Wec.Modules.Inventory

Hardware inventory per scanned computer: CPU, memory banks, physical disks,
operating system, physical network adapters, GPUs, monitors, installed
software and BitLocker protection status. Targets are the local machine or —
via WinRM (ADR 0007) — remote Windows clients.

Fresh, explicitly started Inventory scans may also capture the narrowly
allowlisted user/device relationship evidence from ADR 0019. It is part of the
latest host snapshot, not a separate activity history.

## Bridge actions

| Action | Payload | Result | Notes |
|---|---|---|---|
| `inventory/getHardwareInfo` | `{ forceRefresh?: boolean, target?: TargetRequest, cacheOnly?: boolean }` | `HardwareInfoResult` (host, snapshot, `capturedAtUtc`, `fromCache`) | Snapshot stored in SQLite **per host**; TTL via `Wec:Inventory:CacheTtl`; `cacheOnly` serves the stored snapshot without touching the network (`NOT_FOUND` if none) |
| `inventory/runBatchScan` | `{ hosts, userName?, domain?, password? }` | `InventoryBatchResult` with one typed outcome per host | Explicit remote Inventory capture; bounded by `Wec:Remote:MaxBatchHosts` and `MaxParallelScans`; emits `inventory/batchScanProgress`; cancellation stops queued and active work |
| `inventory/getDiskEncryptionStatus` | `{ target?: TargetRequest }` | `DiskEncryptionStatus` (host, volumes) | Locally requires elevation (`ACCESS_DENIED` + `requiredPrivilege`, ADR 0002); remote rights come from the connection credentials |
| `inventory/listHosts` | `{}` | `{ hosts: [{ host, capturedAtUtc }] }` | Stored snapshots — the UI restores scanned computers across page switches |
| `inventory/deleteHostSnapshot` | `{ host }` | `{ host }` | Removes the stored snapshot for one host |

`TargetRequest` = `{ host?, userName?, domain?, password? }`; empty = local
machine as the current user.

## Structure

- `Domain/` — plain records (`HardwareSnapshot`, `CpuInfo`, `MemoryBank`,
  `DiskDrive`, `OperatingSystemInfo`, `PhysicalNetworkAdapter`, `GpuInfo`,
  `MonitorInfo`, `InstalledSoftwareEntry`, `EncryptableVolume`)
- `Application/` — `HardwareInfoService` (per-host cache lookup → CIM
  queries → persist), bounded `BatchInventoryService`, `DiskEncryptionService`, `InstalledSoftwareReader`
  (registry uninstall keys)
- `Handlers/` — `IActionHandler` implementations, thin delegation to services
- `Persistence/` — `HardwareSnapshotRecord` + EF configuration
  (`inventory_hardware_snapshots`, one cache row per host)

## Design notes

- WMI access goes exclusively through the target-aware `IWmiQueryService`
  overload — the same queries run locally and remotely.
- BitLocker acquisition is shared with Security through the narrow
  `IDiskEncryptionStatusReader`; Inventory only maps its tri-state result to
  the Inventory DTO and does not reinterpret unknown provider state.
- **Installed software** is read from the registry uninstall keys (both
  bitness views), never `Win32_Product` (enumerating it triggers MSI
  reconfiguration). Locally through the registry seam; remotely read-only
  through the WMI `StdRegProv` provider (`RemoteInstalledSoftwareReader`,
  needs remote registry read rights). A failed remote capture sets a
  structured `installedSoftwareError` on the snapshot — never a silently
  empty list.
- **Network adapters** carry MAC, link speed (WMI unknown-sentinels like
  `Int64.MaxValue` normalize to null), IPv4/IPv6 addresses joined from
  `Win32_NetworkAdapterConfiguration`, and sort connected-first.
- **Monitors** (`WmiMonitorID`, `root\wmi`) are optional: headless machines
  and most VMs do not expose the class; that yields an empty list, not a
  failed snapshot.
- Snapshot sections added later are nullable — cache entries written by
  older versions deserialize with those sections as "not captured".
- **User relationship evidence** is limited to the interactive domain account
  and filtered local-profile SID/presence/available last-use data. It never
  reads profile paths or contents and never claims device ownership. Coverage,
  truncation and source failures remain explicit; built-in/system/service
  profiles are removed by tested SID and WMI `Special` rules before
  persistence. The module exposes both a
  SID-to-device projection for User 360 and a stored host-to-observation
  projection for Client 360. Client 360 shows only the named interactive
  observation and aggregates unresolved profile identities; reading either
  projection starts no scan.
- The executive summary report uses the stored snapshot for the selected host;
  omitting the report host selects the local machine.

## Configuration

```jsonc
"Wec": {
  "Inventory": {
    "CacheTtl": "00:15:00",
    "MaxUserProfiles": 100
  },
  "Remote": {
    "ConnectionTimeout": "00:00:30",
    "MaxParallelScans": 4,
    "MaxBatchHosts": 50
  }
}
```

## Tests

`tests/Wec.Modules.Inventory.Tests` — unit tests with mocked seams, incl.
per-host caching, remote snapshots without software, monitor decoding.
`tests/Wec.Infrastructure.IntegrationTests` — per-host persistence round
trip through real migrations on a temp SQLite file.
