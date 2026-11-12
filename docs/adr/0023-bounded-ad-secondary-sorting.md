# ADR 0023: Bounded AD Secondary Sorting

- **Status:** Accepted
- **Date:** 2026-09-16
- **Deciders:** Codex, within the explicit live-defect correction instruction
- **Extends:** ADR 0006, 0019 and 0022

## Context

Live acceptance F-01 showed that AD rejects a two-key LDAP sort control.
Microsoft's [AD protocol specification](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-adts/6b7b93f1-7c1a-45c2-9544-c067b94bba20)
permits only one server sort attribute. Dropping the tie breaker would make
offset pages depend on unspecified ordering of equal or missing values.

## Decision

Send at most one server sort key. Queries requesting a secondary key select
their ordered result prefix in a bounded in-memory priority queue, then return
only the requested window. Compare text ordinally ignoring case, timestamps
in their source order (numeric for lastLogonTimestamp), then the secondary
attribute and finally the full distinguished name. Missing values remain
unknown and sort last ascending, first descending. Never deduplicate by name.

The queue retains at most offset plus page size, capped by the validated
ActiveDirectory MaximumSortedPageEntries option (default 10,000). Reject a
page exceeding this cap before LDAP and instruct the caller to narrow the
query. This refines ADR 0006's page-only retention for secondary-sort queries;
single-key and unsorted/count-only paths retain their existing memory bound.
Counts still describe all entries returned by the explicit filtered search.
No full directory snapshot, persistence, extra attribute or remote write is
introduced. Check cancellation between LDAP pages and entries.

## Alternatives Considered

| Option | Assessment |
| --- | --- |
| Ignore the second key or sort only the returned page | Rejected: equal values can cross independent page boundaries. |
| Materialize and sort the whole directory | Rejected: unbounded retention. |
| Reproduce DC linguistic tie groups | Rejected: depends on server collation/version and needs potentially unbounded tie groups. |
| Bounded ordered prefix | Selected: deterministic across source page order, including duplicate/missing names, with an explicit memory cap. |

## Consequences

Secondary-sort queries decode more entries than the former page-only path,
but retained memory is capped. Very deep pages require a narrower query.
Ordering is deterministic for an unchanged result set; concurrent directory
changes remain live-query changes, not a snapshot-isolation guarantee.
