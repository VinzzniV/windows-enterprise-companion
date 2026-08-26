# ADR 0019: AD-Authoritative User Management

- **Status:** Accepted · revised 2026-08-27 (device relationship evidence)
- **Date:** 2026-08-26
- **Deciders:** Vinz
- **Supersedes:** The former Employee Lifecycle employee record as a future
  identity source; device hygiene in `Wec.Modules.EmployeeLifecycle` remains
  in force

## Context

WEC needs a person/account-oriented User 360 workspace and a safe foundation
for future lifecycle reviews. The existing Employee Lifecycle CRUD model stores
its own employee, department, case, task and audit records beside Active
Directory. Its handlers are no longer registered and its route redirects to
the Clients workspace, but the source types and five SQLite tables remain.
Reactivating that model would create a second, divergent identity truth.

The current `activedirectory/searchUsers` action is read-only but bounded by a
single result limit. It lacks a stable immutable identifier and does not expose
enough lifecycle fields for reliable paging or User 360. Display name, e-mail,
UPN and `sAMAccountName` can all change and therefore cannot be internal keys.

User/client relationships are also evidence, not ownership. The current
providers do not expose an authoritative primary-user assignment, and software
inventory remains device-scoped.

## Decision

1. Create `Wec.Modules.UserManagement` for the read-only Users workspace and
   User 360 composition. Active Directory is the authoritative identity source
   for its first version.
2. Use AD `objectGUID` as the stable internal directory identity. Read the SID
   only where security correlation requires it. Mutable names and addresses
   remain searchable/display fields, never identity keys.
3. Extend the AD-owned projection with an allowlisted, stable server-side page
   contract supporting bounded search, filters and sorting. The MVP reads, when
   available:
   - display name, `sAMAccountName`, UPN, mail and employee ID;
   - department, title, manager DN, distinguished name and OU path;
   - enabled/disabled state, creation and expiration time;
   - replicated `lastLogonTimestamp`, password-last-set and password-expiry
     indicators;
   - direct `memberOf` groups.
4. The cross-module seam is a concrete `IDirectoryUserReadProvider` projection
   in `Wec.Core`, implemented by Active Directory. User Management composes
   additional concrete read projections for client posture, Inventory,
   installed software, Health and Security according to ADR 0004. No generic
   repository, graph or data-source framework is introduced.
5. The first Users workspace is read-only and provides:
   - paginated user search and bounded filtering/sorting;
   - User 360 identity, lifecycle timestamps and direct access context;
   - existing allowlisted privileged-membership warnings;
   - linked-device evidence with source, observed time, relationship type,
     confidence and explanation;
   - device-scoped posture, Health, Security and software summaries with deep
     links to the owning workspaces.
6. User/device evidence never implies ownership. Supported labels are explicit:
   `Assigned`, `Managed by`, `Last interactive user`, `Profile present`, `Local
   administrator` and `Unknown association`. The UI says that software is
   installed on a linked device, not owned by the user.
7. During a deliberately started Inventory scan, WEC may later collect the
   approved interactive domain user and local-profile SID/presence/available
   last-use evidence. Built-in, system and service profiles are filtered by
   tested rules. Profile contents, documents, arbitrary registry payloads and
   separate user-activity history are prohibited. Superseded observations
   follow Inventory retention.
8. `lastLogonTimestamp` is presented as replicated and potentially stale.
   Missing or partial source evidence remains visible and never becomes a
   positive health or lifecycle claim.
9. The former Employee Lifecycle tables
   (`employee_lifecycle_employees`, `employee_lifecycle_departments`,
   `employee_lifecycle_cases`, `employee_lifecycle_tasks` and
   `employee_lifecycle_audit_entries`) remain frozen and preserved. No new code
   writes them, no automatic import treats them as identity truth, and this ADR
   authorizes no drop or destructive migration.
10. After User 360 and correlation, the first lifecycle workflow is a
    read-only **Leaver review**. Joiner and Mover reviews follow only after that
    workflow proves useful. AD account, group, OU or device writes require a
    later ADR covering authorization, preview, confirmation, audit, partial
    failure and rollback.
11. Do not build a generic workflow/checklist engine for the read-only phases.

### Device relationship evidence boundary

The approved Inventory evidence is deliberately smaller than a user-activity
or endpoint-monitoring model:

- A fresh, explicitly started Inventory scan may add one optional
  `DeviceUserEvidence` section to the host's existing latest snapshot. Cache
  reads never collect evidence, and saving a new snapshot replaces the previous
  observation under the existing one-row-per-host retention rule.
- The only collection sources are the allowlisted WMI properties
  `Win32_ComputerSystem.UserName` and `Win32_UserProfile.SID`, `Special` and
  `LastUseTime`. `LocalPath`, profile contents, registry contents, session
  history, logon events and file activity are not read or stored.
- The interactive account is split into domain and account name. A SID may be
  attached only when an exact `Win32_UserAccount` lookup resolves that account;
  an unresolved name remains visible source evidence but is not automatically
  joined to a directory user.
- Local profiles retain only SID, available last-use time and an optional exact
  identity resolved from the same observation. The collection is bounded by a
  validated Inventory option and exposes truncation instead of silently
  claiming complete coverage.
- Filtering rejects WMI profiles marked `Special`, malformed/non-user SIDs,
  well-known system identities (`S-1-5-18`, `S-1-5-19`, `S-1-5-20`), service
  SID authorities (`S-1-5-80-*`, `S-1-5-82-*`) and built-in account RIDs 500,
  501, 503 and 504. These rules are characterized by unit tests; arbitrary
  name-pattern filtering is prohibited.
- Interactive-user and profile queries are optional enrichments. Each source
  records `Available`, `Unavailable` or `NotCaptured` coverage. A source
  failure does not fail the hardware snapshot and is never represented as an
  empty successful result. Snapshots created by older versions deserialize as
  `NotCaptured`.
- Inventory implements a narrow Core read provider that returns only evidence
  matching a requested directory SID. User Management composes this with its
  existing device posture, software, Health and Security read projections; no
  module reference or generic relationship repository is introduced.
- Current relationship types are `Last interactive user` and `Profile present`.
  An exact SID match gives high confidence in the observed interactive account
  and medium confidence in profile presence. Neither confidence level means
  ownership, assignment, primary use or device return. `Assigned`, `Managed
  by` and `Local administrator` remain unavailable until an authoritative
  source exists.
- Every returned relationship carries host, source (`WEC Inventory`), snapshot
  observation time, relationship type, confidence and an explanation. User 360
  displays at most the existing bounded relationship-map limit and links to
  Client 360 for detail.
- Logs may contain the host and aggregate evidence counts, but never account
  names, SIDs, profile timestamps or serialized evidence payloads.

## Alternatives Considered

| Option | Verdict | Reason |
|---|---|---|
| Reactivate Employee Lifecycle CRUD as the user master | Rejected | It duplicates AD identity and can drift from the directory administrators actually operate. |
| Put User 360 inside Active Directory | Rejected | AD analysis owns directory health and access evidence; User 360 composes multiple module projections and is a distinct product workspace. |
| Persist a separate User Management copy of every AD user | Rejected for MVP | Live paged reads avoid stale duplicate identity data; persistence needs a concrete offline or historical requirement. |
| Infer owners from computer names or local profiles | Rejected | Those observations are insufficient evidence for ownership. |
| Add lifecycle write actions immediately | Rejected | Delegated rights, confirmation, audit and rollback have not been designed or authorized. |

## Consequences

- WEC gains one directory-backed user identity instead of two competing master
  records.
- Active Directory must provide a richer stable paged projection, while User
  Management owns composition and presentation rather than LDAP details.
- User/client and user/software views are evidence-aware and can honestly show
  uncertainty, freshness and missing coverage.
- The five legacy Employee Lifecycle tables remain in the EF model and on disk;
  removing them later requires explicit user approval and a separate migration.
- Operational personal data increases only within the approved allowlist and
  Inventory retention boundary.
- All first lifecycle functionality remains read-only; directory mutation is a
  separate future architecture and security decision.
