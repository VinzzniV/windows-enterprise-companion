# Wec.Modules.Clients

Device profiles use the configured bounded local address projections, with
source totals/errors exposed independently. A requested WEC address outside
the initial page gets a targeted bounded read; exact address matches precede
substring matches. Profile composition never enumerates all stored hosts.

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

`clients/getCachedManagementObjectLists` reads the existing raw management cache
through its Core owner seam. The configured working-set limit is shared fairly
across AD, KSC, opsi and Nessus; optional targeted search filters the cached records
before bounding them. Source counts/errors and unknown scopes remain independent.
Native scoped AD IDs are preserved; every original record also gets a disposable
snapshot locator so duplicates and missing native IDs remain individually readable.
`clients/getManagementRecord` resolves exactly that workspace/snapshot/source/index
and fails after replacement. The random snapshot identifier prevents an old link
from referring to another row after application restart. It is not a device ID.
Neither action connects, scans, launches tools or selects an operational endpoint.

The management owner removes cached opsi observations when the active opsi session
changes. Its cache-only check never restores credentials or opens a connection.
The opsi inventory adapter also rejects a completion from a replaced session.
Stored KSC credentials participate in the management cache fingerprint when no
explicit session override is supplied. Replacement/removal and changes during
source I/O invalidate cached completions; serialized fingerprint buffers are
zeroed immediately and connection `ToString` methods redact their fields.

Device profile read composition over concrete Core projections (ADR 0022).
`clients/getOverview` retains its bridge contract, options section and existing
stored Inventory/software/Health/Security/user evidence behavior after extraction
from EmployeeLifecycle. Profile reads perform no Windows scans or probes.

The module references only Core. Source adapters, hygiene assessment, cached
management records and frozen Lifecycle tables remain with their existing owners.
The legacy paged hygiene workbench remains reachable for posture filters, batch
scans and saved targets; the canonical Devices list composes scoped source rows.
No new persistence or data retention is introduced.
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
The seven detail areas are Overview, Identity & management, Inventory, Health &
security, Relationships, Event logs and Printers. Exact Windows tools reuse the
existing implementations and maintain old section URLs. Intune compliance stays
source-labelled and separate from Windows Security. Cloud-only areas never
substitute the local host. Report export and Cleanup remain contextual actions.
