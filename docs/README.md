# Documentation index

Updated: 2026-09-08.

## Read first

1. [Product and execution decisions](../ROADMAP_DECISIONS.md) and
   [accepted architecture decisions](adr/) define the binding boundaries.
2. [Execution status](../ROADMAP_EXECUTION.md) records the last verified
   implementation and outstanding acceptance gates.
3. [Consolidated audit measures](2026-09-08-konsolidierte-massnahmenliste.md)
   are the proposed next implementation backlog, M01-M16. Implementation has
   not started; documentation cleanup does not authorize product changes.
4. [Technical audit](2026-09-08-technischer-ux-audit.md) explains causes,
   affected code, risks and test gaps against commit 17ae57a.
5. [Blind UX audit](2026-09-08-blind-ux-audit.md) is the immutable original
   observation record. Technical conclusions do not rewrite its findings.

The two follow-up reports retain their original German content. Repository
copies use relative source links so they remain usable in other checkouts;
source line references in the original output copies describe the audited commit.

## Architecture and acceptance

- [Product roadmap](ultimate-admin-tool-roadmap.md): implemented phases 0-11
  and product rationale; not a queue to repeat completed work.
- [Foundation architecture](architecture-and-m1-plan.md): historical M1
  structure and dependency rationale, subordinate to later accepted ADRs.
- [Company acceptance](domain-acceptance-2026-09-08.md): recorded evidence and
  remaining external acceptance gates.
- [Active Directory lab](active-directory-lab.md): still-required bounded
  integration and failure-case acceptance.
- Feature contracts remain in module READMEs under src/Modules and in the
  shared frontend UI README.

## Historical notes requiring revalidation

The completed older UX roadmap contained these deferred observations. They
are retained here to avoid silently losing unresolved work; they are not
newly confirmed defects and do not supersede M01-M16.

| Old ID | Follow-up |
| --- | --- |
| N-01 | Investigate the Node test-runner localStorage warning; it also appeared during the technical audit without failing tests. |
| N-02 | Recheck the Dashboard EF multiple-collection include warning and measure the query before choosing a splitting strategy. |
| N-03 | Recheck Nessus IMPORTING_HISTORY completion/progress duration; relate confirmed defects to M02/M15. |
| N-09 | Recheck duplicate KSC exclusion group names on settings load; saving already normalizes them. |

N-04/N-05 concerned retired Employee and standalone device pages. Their removal
is recorded in Phase 11 of the execution register; do not recreate those pages.
N-06/N-07/N-08/N-10 were already marked Done.

## Removed historical documents

The following tracked documents were removed during cleanup; their exact
contents remain available in Git at commit 17ae57a (before this cleanup):

- frontend-ui-ux-audit.md and frontend-ui-ux-roadmap.md: completed older
  22-finding audit and its extensive stage log; distinct from the new blind audit.
- stage-1-audit-2026-08-18.md: pre-roadmap architecture/UX assessment,
  superseded as an active baseline by the implemented roadmap and new audit.
  Removal is not a claim that every historical recommendation was implemented;
  consult Git when a later change touches one of its provider/runtime concerns.
- progress/autonomous-loop-state.md: obsolete milestone loop state with
  contradictory current-status claims.

Do not maintain a second activity log under docs/progress. Update the root
execution register at slice completion, phase boundaries or genuine blockers.
