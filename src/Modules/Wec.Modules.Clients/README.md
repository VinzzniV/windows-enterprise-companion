# Wec.Modules.Clients

`clients/getStoredObjectLists` composes bounded address projections from
Inventory, Security and saved Client-role targets through
`IStoredDeviceListProvider`. Each source reads two SQL queries without loading
snapshot payloads/findings; one failed source leaves other results available.
Native record IDs, full addresses, observation timestamps, individual source
totals and content revisions remain visible. The configurable
`Wec:ObjectWorkingSet:MaximumRecords` defaults to 5000 and also supplies the
overall frontend working-set bound. Optional targeted search is explicit and
literal; SQLite's default LIKE case behavior applies. No Windows/provider scan
or saved-target mutation is performed.

Device profile read composition over concrete Core projections (ADR 0022).
`clients/getOverview` retains its bridge contract, options section and existing
stored Inventory/software/Health/Security/user evidence behavior after extraction
from EmployeeLifecycle. Profile reads perform no Windows scans or probes.

The module references only Core. Source adapters, hygiene assessment, cached
management records and frozen Lifecycle tables remain with their existing owners.
The legacy paged hygiene workbench remains there until the unified device list
has equivalent functionality. No new persistence or data retention is introduced.
# Scoped device profile

`clients/getProfile` composes concrete Core projections for a scoped WEC target,
AD GUID, Entra object ID or Intune managed-device ID. Default reads use stored or
cached facts. AD identity loading is explicit; Graph refresh stays with the
Microsoft365 owner and validates the requested tenant before reading.

Windows tools are available only for an explicitly resolved WEC target. Exact
stored spelling precedes aliases; ambiguous aliases return candidates with no
operational target. Cloud-only profiles never fall back to a local scan.
Entra/Intune foreign IDs create typed links while multiple enrollment records,
duplicates and device-ID conflicts remain visible. Registered owners and Intune
associated users keep their distinct meanings; neither proves human ownership.
KSC/opsi/Nessus name evidence stays in a separate candidate section.

The frontend route is `/devices/:source/:scope/:objectId`. Source facets retain
their retrieval, observation and failed-attempt times. The profile expires its
in-memory cloud copy at source retention and cancels/ignores old subject reads.
Legacy Client tools and routes remain available during the following user/group,
working-set and navigation slices.
