# Legacy branch reconciliation — 2026-09-17

## Scope

This reconciliation compares the unmerged `codex/m01-m16-audit-measures` and
`codex/automatic-client-ping` branches with local `master` at `9992789` after
the object-centered consolidation, UI/UX remediation, Device source-precedence
correction and Microsoft 365 refresh correction.

The old branches cannot be merged directly. Their common base predates ADRs
0021-0023 and the object-centered route and identity model. A direct merge
would reintroduce the superseded canonical-device migration, overwrite the
consolidated navigation and enable a remote Ping while merely opening Client
360. Reconciliation therefore carries forward only still-missing behavior on
top of the current architecture.

## Carried forward

| Historical change | Current reconciliation | Result |
| --- | --- | --- |
| `17ae57a` Kaspersky certificate fingerprint reader | `65b4cfc` | Explicit read-only certificate inspection and tests restored. |
| `56c8519` stored failure semantics | `b10006b` | Unreadable stored data remains distinct from missing data; failed refreshes retain the prior result. |
| `3320a05` comparison coverage | `2b62e40` | Software coverage, version differences, tolerances and evidence are visible. |
| `9722e53` table child keyboard behavior | `b8e730a` | Row activation no longer steals checkbox, button or link interaction. |
| `8e091fc` long-list details | `a982412` | Shared viewport-bound detail dialogs are used by long-list workspaces. |
| `0610421` bounded log coverage | `e718907` | Log windows, result limits and truncation are explicit and configurable. |
| `4400c02` action effects | `292ef57` | Live read effects, duplicate guards and scan previews are explicit. |
| `b2a651c` vulnerability trend evidence | `3830c33` | Trend cohorts, dates, deciding severity and daily values are visible. |
| `b522797` metric populations | `a34b2f4` | Stored hosts, assessed devices and outdated installations use distinct units. |
| `65d6e27` compact client work | `4b2bc74` | Device columns, posture filters and the batch workbench remain usable in compact layouts. |
| `38d1432` side-effect-free inventory reads | `b5aa6ab` | Invalid legacy hosts stay filtered without mutation and list reads are no-tracking. |

## Already present or superseded

- `c276e76`, `b18ce8a` and `d95d61e` are already represented by the current AD
  sorting/log-redaction, explicit BitLocker action and interrupted Nessus sync
  recovery implementations and regression tests.
- `951036a` is superseded by the current lock file. The High/Critical audit gate
  is green; the remaining advisories are the documented Vitest major-upgrade
  deferral.
- `26bb4f2`, `0c17029`, `5698ce8`, `f254615`, `79e3bf5`, `f2ab8c0` and
  `99772f1` are superseded by the current object-centered UI, S1-S8 remediation,
  consolidated route registry, working-set navigation state, source-specific
  data views and newer acceptance evidence. Selected missing behavior from
  these commits was restored by the reconciliation commits above; their old
  page hierarchy and wording were not restored.
- `5ac71d7` and `739f16a` are historical acceptance/progress documentation.
  Their claims describe an older baseline and must not replace the current
  execution and acceptance records.

## Deliberately rejected

- `aba55be` implements a persisted canonical-device identity and atomic
  snapshot model. ADR 0022 instead requires source-scoped identities, in-memory
  composition and explicit evidence for relationships. The migration is not
  compatible with the current product decision.
- `8074582`, `773489a` and `ae3849b` authorize and implement an automatic Ping
  when a relationship map opens. ADR 0018 and the current consolidation require
  Ping/WinRM only after an explicit user action. The branch also assigns ADR
  number 0021, which is now the accepted Microsoft 365 ADR. It must not be
  merged without a new product decision and a newly numbered ADR.
- `6773079` belongs only to the rejected automatic-ping branch and is already
  covered by the current dependency baseline.

## Branch disposition

- `codex/legacy-branch-reconciliation` is the only branch intended for
  fast-forward integration into local `master`.
- `codex/m01-m16-audit-measures` remains as historical evidence after its
  compatible changes have been reconciled. It is not a future merge source.
- `codex/automatic-client-ping` remains intentionally unmerged and blocked by
  the explicit-action product boundary.
- Previously completed consolidation, UI/UX, source-precedence and Microsoft
  365 branches are ancestors of local `master`; their branch names may be
  cleaned up separately without changing product code.

## Verification

The final verification result is recorded in `ROADMAP_EXECUTION.md`. No push,
tag, installer publication or GitHub release is part of this reconciliation.
