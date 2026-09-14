# Wec.Modules.Clients

Device profile read composition over concrete Core projections (ADR 0022).
`clients/getOverview` retains its bridge contract, options section and existing
stored Inventory/software/Health/Security/user evidence behavior after extraction
from EmployeeLifecycle. Profile reads perform no Windows scans or probes.

The module references only Core. Source adapters, hygiene assessment, cached
management records and frozen Lifecycle tables remain with their existing owners.
The legacy paged hygiene workbench remains there until the unified device list
has equivalent functionality. No new persistence or data retention is introduced.
