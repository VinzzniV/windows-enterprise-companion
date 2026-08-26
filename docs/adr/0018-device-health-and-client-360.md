# ADR 0018: Reduce Diagnostics to Device Health and Introduce Client 360

- **Status:** Accepted
- **Date:** 2026-08-26
- **Deciders:** Vinz
- **Supersedes:** The Diagnostics and standalone multi-host placement in ADR
  0010; all other ADR 0010 decisions remain in force

## Context

The current Diagnostics module combines useful operational device evidence
with broad troubleshooting probes. Windows Update age, selected service state,
Event Log summary and free disk space are used when assessing a client. Network
configuration, gateway and DNS probes, domain/DC reachability, time
synchronization and pending-reboot diagnosis duplicate faster established
administrator tools and create permanent maintenance cost.

The Clients workspace is already the canonical device entry point, but its
overview is centered on management-source connectivity. Hardware, software,
security and health evidence remain distributed across detail tabs. Opening a
client must explain the latest known state without automatically starting
expensive remote work.

`Wec.Modules.NetworkScan` is an independent network-discovery product area. It
does not share the Diagnostics lifecycle and must not be removed with
troubleshooting probes.

## Decision

1. The user-visible Diagnostics workspace is renamed **Health**. The backend
   project, namespaces, bridge module name and `diagnostics_runs` table remain
   unchanged during the reduction.
2. Device Health retains local and remote support for:
   - Windows Update recency;
   - configured Windows service states;
   - Event Log summary;
   - free space on fixed disks.
3. Detailed Event Log queries and their existing bridge action remain
   available independently from the Health summary.
4. Remove the following Diagnostics registrations, options, current DTO
   categories and UI results after characterization coverage exists:
   - network configuration and gateway reachability;
   - DNS resolution and DNS-server reachability;
   - domain membership and domain-controller reachability diagnosis;
   - time-synchronization diagnosis;
   - pending-reboot diagnosis.
5. Historical `diagnostics_runs` rows are preserved. Readers ignore removed or
   unknown check codes safely. A schema version is added only if tolerant JSON
   deserialization cannot provide that compatibility. This ADR authorizes no
   destructive migration.
6. Client detail becomes the **Client 360** profile. Its Overview composes
   narrow read projections for latest Inventory, installed software, Health,
   Security and management-source posture. Each projection exposes source,
   timestamp, freshness/coverage and an explicit deep link to the owning
   detail tab.
7. Opening Client 360 reads stored or already loaded evidence only. Missing,
   unavailable and stale data remain distinct and are never interpreted as
   healthy. Remote scans, pings and refreshes require an explicit user action.
8. Cross-module reads follow ADR 0004: contracts are concrete, read-only
   projections in `Wec.Core`, implemented by the data-owning module. Modules do
   not reference one another and no generic data-source framework is created.
9. Useful bounded multi-host Inventory, Security and Health scans move into
   the Clients workspace with explicit selection, operation summary,
   cancellation, progress and per-host typed results. Standalone wrappers and
   their target-selection shell are removed only after behavior parity is
   verified. Global Reporting remains separate.
10. `Wec.Modules.NetworkScan` remains unchanged by this decision.

## Alternatives Considered

| Option | Verdict | Reason |
|---|---|---|
| Remove Diagnostics completely | Rejected | Update age, service state, Event Log and disk capacity are valuable Client 360 evidence. |
| Keep only local troubleshooting | Rejected | The retained WMI/Event Log checks already provide useful remote client assessment under ADR 0007. |
| Hide the existing module without reducing it | Rejected | Unused checks would still carry tests, options, contracts and maintenance cost. |
| Rename the backend project and stored contract immediately | Rejected | It adds broad migration and compatibility risk without user-visible value. |
| Automatically refresh all Client 360 sources on open | Rejected | It creates slow navigation, unexpected remote traffic and misleading partial failure behavior. |

## Consequences

- Health becomes a focused operational signal instead of a general
  troubleshooting toolbox.
- Existing persisted snapshots and bridge compatibility survive the first
  reduction; historical removed results are retained but not shown as current
  checks.
- Client 360 becomes the canonical device profile while detailed feature tabs
  keep domain-specific depth.
- The narrow multi-host placement supersedes ADR 0010's standalone
  Inventory/Security/Diagnostics batch navigation only after parity exists.
- No Network Scan behavior, database table or historical row is removed.
- A later backend rename or historical-data deletion requires a separate,
  explicit decision and migration plan.
