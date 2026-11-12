# Wec.Modules.DeviceCleanup

Read-only guided assessment for stale or orphaned devices.

The module combines concrete Core projections owned by Employee Lifecycle and
Inventory. It shows AD status and replicated last activity, Kaspersky and opsi
last-seen evidence, Nessus scan age, the latest stored WEC Inventory timestamp
and approved user/device observations. Source gaps remain explicit.

`devicecleanup/listCandidates` computes a bounded, searchable and paged list.
Only an explicitly selected subject loads its stored user/device evidence.
Opening the workspace starts no Inventory, Health, Security, Ping or WinRM scan.
Connectivity uses the existing `connectivity/probeHosts` action only after the
administrator requests it in the frontend.

Classification is advisory:

- `PotentialCleanup` requires an existing critical stale-source finding;
- `Review` covers disabled AD computers, stale/orphan signals or an old stored
  Inventory snapshot;
- recent evidence is displayed but never silently overrides another source;
- the administrator makes the final decision and supplies a reason.

Decision, checklist and connectivity state live only in the current frontend
session. `devicecleanup/exportAssessment` writes the explicitly generated,
bounded Markdown summary after a save-dialog confirmation. The module does not
persist workflow state and exposes no AD disable, move or delete action.

`devicecleanup/exportWorkbook` writes every candidate in the active list filter
to one filterable `.xlsx` worksheet after a save-dialog confirmation. It keeps
AD, Kaspersky, opsi, Nessus and stored WEC Inventory timestamps in separate UTC
columns, uses the AD description with an opsi fallback and marks missing values
explicitly. Starting this export sends one bounded ICMP echo to each exported
device. It does not test WinRM, retry, start a scan or treat a missing response
as proof that the device is offline or retired. The export reports when the
configured subject limit made the workbook incomplete.

Ambiguous source observations have individual evidence keys and remain review
rows. An old host-only link that matches several rows asks for an individual
selection; it never takes the first match. Such rows do not load another
subject's stored Inventory and cannot start connectivity probes. Workbook export
retains them and explicitly skips Ping when no unambiguous target exists.
