# Wec.Modules.ActionCenter

Computed, read-only administrative work list defined by ADR 0020.

## Action

- `actioncenter/listItems` — bounded search, source/severity filtering, stable
  sorting and paging over device groups built from current Hygiene plus latest
  stored Inventory and Security evidence. Each group retains its individual
  findings, source timestamps and deep links. Ambiguous source subjects stay
  separate; stored evidence joins a Hygiene subject only on a unique exact
  Windows address.

The module references only `Wec.Core` read projections. It owns no persistence,
ticket, assignee, note, acknowledgement or workflow status. Filling an empty
Hygiene snapshot can reuse the configured bounded AD/Kaspersky/opsi/Nessus
inventory reads; stored Inventory and Security providers never start scans.
Every finding exposes source age, coverage, reliability, a recommended next step
and an allowlisted internal deep link. There are no write or remediation
actions.
