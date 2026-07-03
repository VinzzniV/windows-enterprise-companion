# Wec.Modules.Inventory

Hardware inventory per scanned computer: CPU, memory banks, physical disks,
operating system, physical network adapters, GPUs, monitors, installed
software and BitLocker protection status. Targets are the local machine or —
via WinRM (ADR 0007) — remote Windows clients.

## Bridge actions

| Action | Payload | Result | Notes |
|---|---|---|---|
| `inventory/getHardwareInfo` | `{ forceRefresh?: boolean, target?: TargetRequest }` | `HardwareInfoResult` (host, snapshot, `capturedAtUtc`, `fromCache`) | Snapshot cached in SQLite **per host**; TTL via `Wec:Inventory:CacheTtl` |
| `inventory/getDiskEncryptionStatus` | `{ target?: TargetRequest }` | `DiskEncryptionStatus` (host, volumes) | Locally requires elevation (`ACCESS_DENIED` + `requiredPrivilege`, ADR 0002); remote rights come from the connection credentials |

`TargetRequest` = `{ host?, userName?, domain?, password? }`; empty = local
machine as the current user.

## Structure

- `Domain/` — plain records (`HardwareSnapshot`, `CpuInfo`, `MemoryBank`,
  `DiskDrive`, `OperatingSystemInfo`, `PhysicalNetworkAdapter`, `GpuInfo`,
  `MonitorInfo`, `InstalledSoftwareEntry`, `EncryptableVolume`)
- `Application/` — `HardwareInfoService` (per-host cache lookup → CIM
  queries → persist), `DiskEncryptionService`, `InstalledSoftwareReader`
  (registry uninstall keys)
- `Handlers/` — `IActionHandler` implementations, thin delegation to services
- `Persistence/` — `HardwareSnapshotRecord` + EF configuration
  (`inventory_hardware_snapshots`, one cache row per host)

## Design notes

- WMI access goes exclusively through the target-aware `IWmiQueryService`
  overload — the same queries run locally and remotely.
- **Installed software** is read from the registry uninstall keys (both
  bitness views), never `Win32_Product` (enumerating it triggers MSI
  reconfiguration). Registry access is local-only, so remote snapshots have
  `installedSoftware: null` — shown as such, never silently empty.
- **Monitors** (`WmiMonitorID`, `root\wmi`) are optional: headless machines
  and most VMs do not expose the class; that yields an empty list, not a
  failed snapshot.
- Snapshot sections added later are nullable — cache entries written by
  older versions deserialize with those sections as "not captured".
- The executive summary report always uses the **local** host's snapshot.

## Configuration

```jsonc
"Wec": {
  "Inventory": { "CacheTtl": "00:15:00" },
  "Remote": { "ConnectionTimeout": "00:00:30" }
}
```

## Tests

`tests/Wec.Modules.Inventory.Tests` — unit tests with mocked seams, incl.
per-host caching, remote snapshots without software, monitor decoding.
`tests/Wec.Infrastructure.IntegrationTests` — per-host persistence round
trip through real migrations on a temp SQLite file.
