# ADR 0020: Computed Action Center Read Model

- **Status:** Accepted
- **Date:** 2026-08-27
- **Deciders:** Vinz
- **Supersedes:** —

## Context

WEC already computes device posture from Active Directory, Kaspersky, opsi and
Nessus and stores latest Inventory and Security evidence. Administrators need
one prioritized work list, but duplicating those findings into ticket-like
records would introduce another state model, synchronization rules and stale
ownership data before a real workflow requirement exists.

The modular-monolith rules prohibit Action Center from referencing the modules
that own this evidence. Invoking their bridge handlers in-process would couple
the implementation to transport contracts. Reading their EF entities would
couple it to persistence details. The existing Core read-provider pattern in
ADR 0004 is the approved seam.

Some source inventory is loaded from configured company systems, whereas
Inventory and Security projections are stored snapshots. Source failure,
partial coverage and evidence age must remain visible. Opening the work list
must never start a per-device scan or imply that missing evidence is healthy.

## Decision

1. Add `Wec.Modules.ActionCenter` as a read-only composition module. Its first
   version exposes a bounded, searchable, sortable and server-paged work list.
   It owns no EF entities, migrations, status history, assignee, note or
   workflow state.
2. Work items are deterministic projections of current evidence. Their stable
   key is derived from the evidence source, finding code and stable subject
   key. Recalculation may add, change or remove an item; this is expected and
   is not a persisted lifecycle transition.
3. The MVP item contains:
   - affected device and, only with approved exact evidence, an optional user;
   - problem/finding code and human-readable explanation;
   - source and source timestamp;
   - severity;
   - evidence age;
   - coverage or reliability with an explanation;
   - recommended next action;
   - an allowlisted deep link into the owning WEC workspace.
4. Cross-module reads use concrete contracts in `Wec.Core`:
   - Employee Lifecycle exposes the already computed AD/Kaspersky/opsi/Nessus
     hygiene findings and source states through an Action-Center-specific read
     projection;
   - Inventory exposes latest stored host timestamps through its existing
     client-snapshot projection unless additional concrete evidence is proven
     necessary;
   - Security exposes a bounded aggregate projection of latest stored scan
     findings so Action Center does not perform an unbounded per-host N+1 read.
   The owning modules implement these contracts. No generic evidence provider,
   repository or rule engine is introduced.
5. Extract the pure hygiene assessment policy from `ItHygieneService` before
   sharing its output. Characterization tests preserve finding precedence,
   severity, stale thresholds, missing/orphan semantics and source-availability
   behavior. Loading and caching external source inventories remain separate
   from the pure assessment.
6. Action Center reuses the existing request-bound hygiene snapshot and its
   explicit `force` refresh semantics. Filling an empty snapshot may perform
   the same bounded configured AD/Kaspersky/opsi/Nessus inventory reads as the
   Clients workspace. It does not start Inventory, Health, Security, Ping,
   WinRM or Nessus synchronization per device. Stored evidence providers never
   refresh their owning source.
7. Missing, unavailable, partial and truncated coverage are distinct from an
   empty successful result. The response includes source-level state and
   assessment time. Unknown evidence cannot produce a positive health claim.
8. Severity normalization is explicit and tested. Critical vulnerability or
   critical hygiene evidence ranks above warning evidence; incomplete coverage
   remains visibly unknown and does not outrank a confirmed critical finding.
   Ties use deterministic source, subject and finding-code ordering.
9. User links are omitted until an exact relationship already authorized by
   ADR 0019 matches the affected device. Names, e-mail addresses and host-name
   conventions are never used to infer a user. The first table may ship
   without user links.
10. Deep links are generated from an allowlisted mapping owned by Action
    Center. Evidence text is never treated as a route. The MVP contains no
    write action, automatic remediation, arbitrary command or external URL.
11. Search, filter, sort and paging are applied to the computed projection with
    validated limits. Configuration owns stale-age thresholds and maximum
    result sizes; credentials and raw source payloads never enter work items,
    logs or persistence.
12. The optional compact Relationship Map is deferred until the table workflow
    is complete. It remains a frontend view of one selected work item's
    existing evidence and is not required to use Action Center.

## Alternatives Considered

| Option | Verdict | Reason |
|---|---|---|
| Persist work items with status, owner and notes | Rejected for MVP | It adds synchronization and workflow semantics without established operational use. |
| Build Action Center only in the frontend from bridge calls | Rejected | It duplicates normalization and paging logic and couples the UI to several feature transports. |
| Let Action Center read other modules' EF entities | Rejected | Persistence remains owned by each module and would violate ADR 0004. |
| Add one generic evidence-provider interface | Rejected | Different source semantics would be erased behind a speculative abstraction. |
| Automatically run remote scans for every item | Rejected | It creates unexpected traffic, latency and misleading partial results. |
| Build the context map before the work list | Rejected | The operational table, filters and deep links provide the primary value. |

## Consequences

- Action Center remains current and disposable because every item can be
  recomputed from owning source evidence.
- Employee Lifecycle's large service gains a tested assessment boundary before
  Action Center and stale-device cleanup reuse it.
- Concrete Core projections increase deliberately, but modules remain isolated
  and no generic cross-module framework is created.
- The first load can still depend on configured company inventories; source
  state and assessment time make that dependency explicit and tests can use
  pure projections in the home environment.
- Persisted acknowledgements, ownership and workflow history require a future
  product decision and ADR rather than an incremental field addition.
