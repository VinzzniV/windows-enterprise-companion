# Wec.Modules.Inventory

Hardware inventory for the local machine: CPU, memory banks, physical disks,
operating system, and BitLocker protection status.

## Bridge actions

| Action | Payload | Result | Notes |
|---|---|---|---|
| `inventory/getHardwareInfo` | `{ forceRefresh?: boolean }` | `HardwareInfoResult` (snapshot, `capturedAtUtc`, `fromCache`) | Snapshot is cached in SQLite; TTL via `Wec:Inventory:CacheTtl` |
| `inventory/getDiskEncryptionStatus` | `{}` | `DiskEncryptionStatus` (volumes with protection status) | Requires elevation; unelevated calls fail with `ACCESS_DENIED` + `requiredPrivilege: ADMINISTRATOR` (ADR 0002) |

## Structure

- `Domain/` — plain records (`HardwareSnapshot`, `CpuInfo`, `MemoryBank`,
  `DiskDrive`, `OperatingSystemInfo`, `EncryptableVolume`)
- `Application/` — `HardwareInfoService` (cache lookup → CIM queries →
  persist), `DiskEncryptionService` (privilege gate → CIM query)
- `Handlers/` — `IActionHandler` implementations, thin delegation to services
- `Persistence/` — `HardwareSnapshotRecord` + EF configuration
  (`inventory_hardware_snapshots`), `IHardwareSnapshotRepository` +
  EF implementation

## Design notes

- **Dependency rule:** this module references only `Wec.Core` (plus NuGet
  packages). The repository works against the `DbContext` *base type*; the
  host aliases its `WecDbContext` into the scope.
- **Cache shape:** the snapshot table is a single-entry cache
  (replace-on-save) storing the snapshot as a JSON payload. There is no
  requirement to query hardware properties relationally; if that changes,
  revisit the schema with a migration.
- `captured_at_utc` is stored as UTC ticks (INTEGER) because SQLite cannot
  order `DateTimeOffset` columns.
- WMI access goes exclusively through `IWmiQueryService` (Core abstraction),
  which keeps every service unit-testable without WMI.

## Configuration

```jsonc
"Wec": {
  "Inventory": {
    "CacheTtl": "00:15:00"
  }
}
```

## Tests

`tests/Wec.Modules.Inventory.Tests` — unit tests with mocked
`IWmiQueryService`/`IPrivilegeContext`/repository/`IClock`.
`tests/Wec.Infrastructure.IntegrationTests` — persistence round trip through
real migrations on a temp SQLite file.
