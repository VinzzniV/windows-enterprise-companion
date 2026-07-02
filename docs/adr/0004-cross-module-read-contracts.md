# ADR 0004: Cross-Module Read Contracts in Wec.Core

- **Status:** Proposed (implemented with M5 slice 1; user review pending)
- **Date:** 2026-07-02
- **Deciders:** Vinz
- **Supersedes:** —

## Context

The Reporting module (M5) needs the latest hardware inventory snapshot and the
latest security scan. Dependency rule 2 forbids module-to-module references.
The architecture plan (§3, rule 5) anticipated this moment: cross-module
communication goes through contracts defined in `Wec.Core`, decided per case
with its own ADR. This is that case.

## Decision

**Read-only provider contracts live in `Wec.Core.Contracts`.**

- The **data-owning module implements** the contract
  (`Wec.Modules.Inventory` → `IInventoryReportDataProvider`,
  `Wec.Modules.Security` → `ISecurityReportDataProvider`) and registers the
  implementation in its `IModule.RegisterServices`.
- **Consuming modules depend only on the Core interface.** The host wires
  everything; no module references another module.
- The contract DTOs (`InventoryReportData`, `SecurityReportData`, …) are
  **report-focused shapes, not domain mirrors**. Enums cross the boundary as
  strings plus a numeric rank where ordering matters (`SeverityRank`), so the
  owning module's enums stay private to it.
- The moderate duplication between module domain records and contract DTOs is
  deliberate: it decouples the owning module's domain evolution from every
  consumer, at the cost of a mapping method per provider.

## Alternatives Considered

| Option | Verdict | Reason |
|---|---|---|
| Direct project reference Reporting → Inventory/Security | Rejected | Violates dependency rule 2; starts the coupling spiral the module cut is there to prevent |
| Move module domain records into Core | Rejected | Core stays minimal contracts; otherwise it accumulates every module's domain model and every domain change becomes a Core change |
| Invoke other modules' bridge handlers in-process | Rejected | Couples modules to wire-contract action names and envelope semantics; handlers are a transport surface, not an internal API |
| Shared read access to the other modules' EF entities | Rejected | Entities are persistence details owned by their module |

## Consequences

- One obvious, repeatable pattern for future cross-module reads
  (M5 JSON export, later dashboard/AI modules read the same contracts).
- Providers are trivially unit-testable (mapping only) and consumers mock the
  Core interface.
- Contract DTOs must be evolved consciously; adding fields is cheap, renames
  are breaking for all consumers.
- Events (push-style cross-module communication) remain undecided — separate
  ADR when a feature needs them.
