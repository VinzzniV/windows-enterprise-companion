# Windows Enterprise Companion — Consolidated Product and Implementation Roadmap

Status: phases 0–11 implemented on `codex/ultimate-admin-roadmap`; final GitHub
artifact-upload verification and merge remain pending

Repository baseline: implementation branch created from `origin/master` at
`aba4ccd57f724cb359e9ac643378bf6ada0ce559`

Created: 2026-08-26

Implementation status last verified: 2026-08-27. Detailed slice, test and
external-gate evidence is maintained in `ROADMAP_EXECUTION.md`.

## 1. Purpose

This document consolidates the verified repository analysis, the existing
implementation plan and the following updated product direction:

- keep the useful local and remote device-health checks;
- replace the broad Diagnostics workspace with a focused health view;
- create a comprehensive device overview that brings hardware, software,
  management sources and operational health together;
- introduce read-only User Management as the foundation for a future user
  lifecycle;
- connect users, clients, software, access and source evidence into coherent
  device and user profiles;
- rework the existing client integration map into a reusable, evidence-aware
  relationship map;
- continue with release hardening, dependency security, Action Center, the
  stale-device cleanup assistant, lazy loading, global search, legacy cleanup
  and targeted service refactoring.

The plan deliberately keeps the approved modular-monolith architecture. It
does not introduce a web server, plugin runtime, workflow engine, event bus,
microservices or automatic remediation.

## 2. Product model

The product should converge on four primary operational workspaces instead of
accumulating more unrelated dashboards.

### 2.1 Client 360

One canonical profile for a device. Its overview answers:

- What is this device?
- Is it reachable and sufficiently current?
- Which management systems know it?
- What hardware and operating system does it have?
- Which software is installed?
- What is unhealthy or security-relevant?
- Which users are reliably associated with it?
- What should the administrator inspect next?

The overview is a summary and navigation surface, not a replacement for all
detailed tabs. It uses the latest known snapshots and must not automatically
start every expensive remote query when opened.

### 2.2 User 360

One canonical profile for an Active Directory identity. Its overview answers:

- Who is this user and is the account active?
- Where is the user located in the directory and organization?
- Which direct groups and privileged relationships exist?
- When was the account created, last used and last changed?
- Which clients are assigned to or observed for the user, and how reliable is
  that relationship?
- What software and risk context exists on those linked clients?
- Which lifecycle or hygiene issues require attention?

Active Directory is the authoritative identity source for the first version.
WEC must not create a second independent employee master-data truth.

### 2.3 Action Center

A computed, read-only administrative work list built from existing source
evidence. It links directly into Client 360, User 360, Vulnerabilities, Patch
Management and the cleanup assistant. It is not a ticket system and does not
persist owners, notes or workflow states in its first version.

### 2.4 Guided assistants

The stale-device cleanup assistant is the first guided decision workflow. It
collects evidence, explains a classification, records a manual decision for
the current session and exports a checklist. It never disables, moves or
deletes an AD object in its first version.

Future user lifecycle assistance follows the same principle: prepare and
explain first; introduce directory writes only through a later, separately
approved architecture and authorization decision.

## 3. Revised device-health direction

### 3.1 Checks that remain

The following checks remain available for both local and remote clients:

- Windows Update age;
- selected Windows service states;
- Event Log summary;
- free disk space.

The detailed Event Log query remains available independently from the summary.
The Event Log tab is therefore retained.

### 3.2 Checks that are removed

Remove the checks that are not used in normal administration:

- network configuration;
- gateway reachability;
- DNS resolution;
- DNS server reachability;
- domain membership diagnosis;
- domain controller reachability;
- time synchronization diagnosis;
- pending-reboot diagnosis.

`Wec.Modules.NetworkScan` remains untouched. Network discovery through nmap and
DHCP is a separate product area and must not be treated as Diagnostics.

### 3.3 Naming and information architecture

Rename the visible Diagnostics tab to **Health**. The retained checks describe
device health more accurately than broad troubleshooting diagnostics.

Keep the backend project and bridge module name `Wec.Modules.Diagnostics` in the
first reduction slice. Renaming the entire project, namespace, bridge actions,
tests and persisted JSON would add migration risk without user-visible value.
The module README and options must clearly state its reduced Device Health
scope. A backend rename can be reconsidered only if the remaining module later
grows into a stable broader domain.

Recommended Client 360 tab structure:

1. **Overview** — identity, relationship map, source status, hardware/OS
   summary, software summary, health summary, security/posture and linked
   users;
2. **Inventory** — complete hardware and operating-system data;
3. **Software** — complete installed-software list and patch context;
4. **Health** — the four retained checks, last run and explicit refresh;
5. **Security** — detailed security scan and coverage;
6. **Events** — detailed Event Log query;
7. **Printers** — client printer information;
8. **Reports** — generated client reports.

If splitting Inventory and Software would duplicate the current data-loading
path, keep one Inventory tab with Hardware and Software sub-sections initially.
The Overview still exposes both as separate summaries.

### 3.4 Overview behavior

The Client 360 Overview should use stored or already loaded data:

- latest Inventory snapshot and capture time;
- installed-software count, important package status and snapshot time;
- latest Health result and its age;
- latest Security result and coverage;
- AD, Kaspersky, opsi and Nessus posture;
- current connectivity evidence only when already known or explicitly
  requested;
- linked-user evidence when available.

Missing data is shown as `Not scanned`, `Not connected` or `Unknown`; it must
never be silently interpreted as healthy. Every summary has a link to its
detailed tab and an explicit refresh or scan action where appropriate.

### 3.5 Persistence

Continue using the existing `diagnostics_runs` table for the reduced Health
snapshot. Historical JSON can contain removed check codes; the new reader must
ignore unsupported codes safely. Do not delete the table or old rows.

The schema should gain an explicit result/schema version only if the existing
JSON deserialization cannot safely tolerate the reduced result set. No data
drop migration is part of this roadmap.

## 4. User Management and future User Lifecycle

### 4.1 Architectural placement

Create a new `Wec.Modules.UserManagement` module. Do not reactivate the old
Employee Lifecycle CRUD workflow as the new source of truth.

Reasons:

- the old workflow stores a parallel employee record beside Active Directory;
- its handlers are currently not registered and its pages are not routed;
- a real user profile must start with the actual directory identity;
- device, software and security relationships are read models, not employee
  master data owned by the old tables.

The old Employee Lifecycle source and tables remain frozen until the new user
model is implemented and any reusable concepts have been deliberately
migrated. They must not be deleted in the early legacy-cleanup slice.

The new module references only `Wec.Core`. Cross-module reads use narrow Core
contracts implemented by the source-owning modules:

- `IDirectoryUserReadProvider`, implemented by Active Directory;
- existing installed-software and inventory providers;
- a narrow client-posture provider implemented by Employee Lifecycle;
- a future client-user-evidence provider implemented by the module that owns
  the collected evidence.

Avoid a generic data-source framework. Each contract should represent one
concrete business projection.

### 4.2 Read-only MVP

The first User Management version contains:

- server-side, paginated user search;
- search by display name, `sAMAccountName`, UPN and optionally employee ID;
- enabled/disabled and OU/department filters;
- stable sorting and result limits;
- a User 360 detail route;
- account identity and lifecycle timestamps;
- direct group memberships;
- privileged membership warnings based on the existing allowlisted AD logic;
- manager and department information where AD provides it;
- linked devices with relationship type, source, observed time and confidence;
- device posture, health and security summaries for linked devices;
- software summaries from linked devices;
- read-only deep links into the relevant client, group, patch or security view;
- inclusion in the global `Ctrl+K` search.

The current `activedirectory/searchUsers` action is a useful starting point but
is capped and materializes a bounded result list. User Management needs a new
stable server-side page contract rather than increasing that cap.

### 4.3 User identity model

The MVP should read at least these AD attributes, subject to directory
availability:

- `objectGUID` as the stable directory identity;
- SID where required for security correlation;
- display name, `sAMAccountName` and UPN;
- mail, employee ID, department and title;
- distinguished name and OU path;
- enabled/disabled state;
- account creation and expiration;
- `lastLogonTimestamp`, clearly labelled as replicated and potentially stale;
- password-last-set and password-expiry indicators;
- manager distinguished name;
- direct `memberOf` groups.

Do not use a display name, e-mail address or mutable account name as the
internal identity key.

### 4.4 User-to-client relationships

WEC must distinguish relationship evidence instead of claiming that every
observed account owns a device.

Supported relationship labels should be explicit:

- **Assigned** — supplied by an authoritative assignment source;
- **Managed by** — derived from an explicit directory relationship;
- **Last interactive user** — observed on the device at a stated time;
- **Profile present** — a local user profile exists on the device;
- **Local administrator** — the account is present in the local Administrators
  group;
- **Unknown association** — evidence exists but is not strong enough for an
  ownership statement.

Each relationship contains:

- source;
- observation timestamp;
- confidence or evidence quality;
- whether the relationship is direct or inferred;
- a short explanation.

The current providers do not expose a reliable primary-user assignment. The
approved extended evidence scope allows an explicit Inventory scan to collect
the interactive domain user together with local user-profile SIDs and their
available last-use timestamps. This evidence is persisted only as part of the
latest host inventory projection under the documented retention policy.

Built-in, service and system profiles are filtered through explicit,
well-tested rules. Raw registry paths and profile contents are not collected.
The resulting relationships remain observations, never ownership claims, and
WEC must not guess an owner from host naming conventions.

### 4.5 Software and risk connection

Installed software remains device-scoped. User 360 may show:

- software installed on linked devices;
- package count and snapshot age per device;
- outdated or vulnerable software summaries;
- links to the device Software, Patch Management or Vulnerabilities view.

The UI must say `Installed on linked device` rather than `Software owned by the
user`. User-specific license assignment or entitlement is a separate data
source and is not inferred from device inventory.

### 4.6 Future lifecycle phases

After the read-only profile proves useful, the first lifecycle workflow is the
read-only **Leaver review**:

1. **Leaver review** — disabled state, remaining groups, linked-device return
   evidence and outstanding access are presented without mutation;
2. **Lifecycle readiness** — start date, end date, expected department,
   manager and required access can later be compared with AD without writing;
3. **Joiner checklist** — missing account, groups, assigned device and required
   software shown as a reviewable plan;
4. **Mover checklist** — department, manager, groups, devices and software
   differences;
5. **Controlled actions** — only after a new ADR covering permissions,
   approvals, preview, confirmation, audit and rollback.

Do not build a generic checklist/workflow engine in the read-only phases.

## 5. Relationship Map rework

### 5.1 Current limitations

The existing `ClientIntegrationMap` is fixed to one client and four sources.
Its node coordinates, connectors and source types are hardcoded. Nodes can be
dragged and spring back, but that interaction does not help an administrator
make a decision. It also starts a ping automatically when the component opens.

This implementation should not be expanded by adding more hardcoded nodes for
users, hardware and software.

### 5.2 Recommended direction

Build a reusable frontend `RelationshipMap` with explicit view models for a
small number of supported contexts. This is not a generic graph engine and has
no generic graph persistence.

Each node contains:

- entity type and stable key;
- primary label and compact secondary context;
- semantic status;
- freshness timestamp;
- optional deep link.

Each edge contains:

- relationship type;
- evidence source;
- observation timestamp;
- confidence;
- short explanation.

The map must provide:

- keyboard-focusable, clickable nodes;
- no required drag interaction;
- a semantic list/table fallback containing the same information;
- loading, partial, unavailable and stale states;
- explicit refresh instead of automatic network probing;
- `prefers-reduced-motion` support;
- a bounded node count and aggregation nodes such as `12 groups` or
  `84 software packages` instead of rendering every object.

### 5.3 Supported map contexts

#### Device relationship map

Center: the client.

First relationships: AD, Kaspersky, opsi, Nessus and WEC Inventory.

Optional linked entities: reliably associated users.

Aggregated context: hardware, software, health and security status.

This replaces the current fixed integration map inside Client 360.

#### User relationship map

Center: the AD user.

First relationships: linked devices.

Secondary context: manager, department, direct groups and privileged access.

Device nodes expose compact health/security posture and link to Client 360.

#### Action Center context map

Optional compact map in an Action Center detail drawer. It shows only the
affected user/device, the reporting source and the relevant relationship. It
must not be required to use the work list.

#### Later department/team map

A bounded department view can show manager, user count, assigned-device count
and lifecycle exceptions. It should aggregate users rather than attempt to
render the entire directory.

### 5.4 Where not to use it

Do not create a single unrestricted environment-wide graph containing every
user, client, group and software package. It would be visually impressive but
operationally noisy, slow and difficult to make accessible. Fleet lists,
filters and the Action Center remain better for large-scale work.

### 5.5 Interface direction

Domain concepts: identity, endpoint, evidence, management source, freshness,
ownership, access and lifecycle.

Color world: graphite console surfaces, directory blue, endpoint cyan,
verified green, stale amber and critical red. Color remains semantic rather
than decorative.

Signature: every relationship can reveal `why WEC believes this` together with
source and timestamp.

Rejected defaults:

- more large dashboard cards — use dense summaries and drill-downs;
- a decorative draggable node graph — use actionable nodes and evidence;
- an unlimited topology canvas — use bounded context-specific maps.

The desired feeling is a calm but information-dense administration workbench.
One primary identity sits at the center; uncertainty and stale evidence are
visible without overwhelming the operator.

## 6. Navigation and global search

Recommended primary navigation after the new workspaces exist:

1. Overview
2. Action Center
3. Clients
4. Users
5. Active Directory
6. Vulnerabilities
7. Patch Management
8. Print Management
9. Network Scan
10. Reporting
11. Settings
12. Error Log

`Users` and `Active Directory` have different purposes:

- **Users** is the person/account-oriented User 360 workspace;
- **Active Directory** remains directory health, hygiene, privileged groups
  and infrastructure context.

Global `Ctrl+K` search should eventually cover:

- navigation and modules;
- clients;
- users;
- saved targets;
- safe read-only destinations such as `Open latest health` or `Show linked
  devices`.

No write operation appears in the first command palette.

## 7. Relationship to the existing roadmap

### Release pipeline

Keep the existing recommendation:

- resolve the GitHub artifact-storage quota organizationally;
- reduce short-lived master artifact retention;
- avoid duplicate long-term tag artifacts;
- verify ZIP, installer and checksums;
- keep every build, test and published-host smoke gate;
- handle code signing separately.

### NPM security

Patch the four current High-Severity findings and add
`npm audit --audit-level=high` to CI. Do not perform unnecessary major upgrades.

### Action Center

Keep the computed read-only MVP. Add user context only when a relationship is
supported by explicit evidence. Action items may link to either Client 360 or
User 360.

### Stale-device cleanup assistant

Keep the read-only evidence and export approach. User relationships can become
additional evidence, for example an active recent user observation on an old
AD computer, but must not automatically overrule the existing source facts.

### Lazy loading

Introduce route-based lazy loading before adding User Management and Action
Center routes. This prevents the new workspaces from increasing the initial
bundle again.

### Legacy cleanup

Adjust the earlier cleanup decision:

- Diagnostics is reduced, not removed;
- detailed Event Logs remain;
- Network Scan remains;
- old Employee Lifecycle CRUD is not reactivated;
- old Employee Lifecycle source and tables are temporarily retained until the
  User Management model is complete;
- multi-host Inventory, Security and Health scans are integrated into the
  canonical Clients workspace with bounded selection, progress and
  cancellation;
- unrouted Inventory, Security and Diagnostics wrappers are removed only after
  their reusable components and batch behavior have been migrated;
- the full `TargetSelector` can be removed only after its remaining consumers
  are eliminated.

### Large services

Keep the targeted split plan:

- extract pure hygiene assessment from `ItHygieneService` before Action Center
  and cleanup work;
- extract source loading separately;
- split `WingetPackageService` into planning and execution boundaries after
  characterization tests;
- do not split generated contracts, migrations or cohesive pages solely by
  line count.

## 8. Recommended implementation sequence

### Phase 0 — Stabilize the delivery baseline

1. Patch vulnerable NPM packages.
2. Add the NPM audit gate.
3. Resolve the GitHub artifact quota.
4. Reduce artifact retention and verify packages/releases.

Effort: **S–M**.

### Phase 1 — Record the revised product decisions

1. Add an ADR for the reduced Device Health scope and Client 360 overview.
2. Add an ADR for AD-authoritative User Management and future lifecycle.
3. Document that old Employee Lifecycle data remains frozen and preserved.

Effort: **S**.

### Phase 2 — Reduce Diagnostics into Device Health

1. Add characterization tests for the four retained checks and Event Log
   query.
2. Remove the other diagnostic registrations, options, DTO categories and
   tests.
3. Make historical result deserialization tolerant of removed check codes.
4. Rename the visible tab to Health.
5. Keep `diagnostics_runs` and existing bridge/module names initially.

Suggested commits:

- `test: characterize retained device health checks`
- `refactor: reduce diagnostics to device health`
- `refactor: rename diagnostics workspace to health`

Effort: **M**.

### Phase 3 — Build Client 360 Overview

1. Define a narrow overview read model using existing Core providers.
2. Add hardware, OS, installed-software, Health and Security summaries.
3. Preserve detailed tabs and add reliable deep links.
4. Never trigger all remote scans automatically.
5. Expose linked-user evidence only when the relationship contract exists.

Suggested commits:

- `refactor: define client overview read model`
- `feat: add device inventory and health summaries`
- `feat: add client software and security context`

Effort: **L**.

### Phase 4 — Route-based lazy loading and route registry

1. Extract route/navigation metadata.
2. Lazy-load all top-level workspaces.
3. Add shared Suspense and Error Boundary behavior.
4. Record initial bundle size before and after.

Suggested commit: `perf: lazy load application routes`.

Effort: **S**.

### Phase 5 — Relationship Map foundation

1. Add characterization tests for current source-state mapping.
2. Introduce bounded `RelationshipNode` and `RelationshipEdge` frontend view
   models.
3. Build accessible map and list presentations.
4. Replace decorative dragging and automatic ping with actions and deep links.
5. Migrate Client 360 to the new device context map.

Suggested commits:

- `test: characterize client integration map states`
- `feat: add accessible relationship map`
- `refactor: migrate client overview to relationship map`

Effort: **M–L**.

### Phase 6 — User Management read-only foundation

1. Extend the AD user projection with stable identifiers and lifecycle fields.
2. Add stable server-side user paging, search, filtering and sorting.
3. Create `Wec.Modules.UserManagement` and its User 360 read model.
4. Build Users list and User 360 Overview/Access tabs.
5. Add users to global search.

Suggested commits:

- `feat: add paged directory user inventory`
- `feat: add read-only user management module`
- `feat: add users workspace`
- `feat: add user profiles to global search`

Effort: **L–XL**.

### Phase 7 — User/client/software correlation

1. Collect the approved interactive-user and local-profile evidence only during
   explicit Inventory scans.
2. Add explicit evidence DTOs, filtering and source implementations.
3. Correlate users to clients without guessing ownership.
4. Surface linked-device posture, software and vulnerability summaries.
5. Add the User relationship map.

Suggested commits:

- `docs: define user device relationship evidence`
- `feat: collect client user relationship evidence`
- `feat: connect user profiles with client context`
- `feat: add user relationship map`

Effort: **XL**, because the reliable source evidence is not yet present.

### Phase 8 — Read-only Leaver review

1. Add a Leaver review entry point for a deliberately selected user.
2. Show account state, last activity, remaining direct and privileged groups,
   linked devices, device-return evidence and unresolved access context.
3. Explain missing and partial source evidence.
4. Export a review checklist without persisting workflow state.
5. Do not disable accounts, remove groups or mutate devices.

Suggested commits:

- `feat: add read-only leaver assessment`
- `feat: add leaver review checklist`

Effort: **M–L**.

### Phase 9 — Action Center

1. Extract and characterize hygiene assessment boundaries.
2. Add the computed Action Center module.
3. Start with AD/Kaspersky/opsi/Nessus posture, Inventory and Security.
4. Add user links only for sufficiently reliable correlations.
5. Add the optional compact context map after the table workflow is complete.

Effort: **L–XL**.

### Phase 10 — Stale-device cleanup assistant

1. Reuse hygiene, Inventory and source timestamps.
2. Add explicit user relationship evidence when available.
3. Keep connectivity checks user-triggered.
4. Add manual decision, required reason and safe export.
5. Do not add AD writes.

Effort: **L**.

### Phase 11 — Legacy cleanup and service refactoring

1. Add bounded Inventory, Security and Health bulk actions to Clients with
   explicit selection, progress, cancellation and per-host results.
2. Extract reusable components and remove the replaced standalone wrappers
   only after behavior parity is verified.
3. Reassess old Employee Lifecycle code only after User Management MVP.
4. Preserve old tables until a separately approved migration.
5. Complete `ItHygieneService` and `WingetPackageService` splits with unchanged
   public contracts.

Effort: **L**.

## 9. Data, privacy and security rules

- Credentials remain request-scoped or in the existing secure credential
  store; never in user/device read models.
- Passwords, tokens and raw credential objects are never persisted or logged.
- User identifiers and device relationships are operational personal data;
  only attributes needed for administration are collected.
- Interactive-user and local-profile evidence may be persisted in the latest
  Inventory projection. Superseded observations follow the Inventory retention
  policy and are not copied into a separate user-history store.
- Local-profile collection is limited to SID, resolved domain identity when
  available, profile-presence evidence and available last-use time. Profile
  contents, documents and registry payloads remain out of scope.
- AD `lastLogonTimestamp` is labelled as replicated evidence, not exact truth.
- A local profile or last interactive user does not prove device ownership.
- Software inventory is device-scoped unless a future authoritative license or
  user-assignment provider states otherwise.
- Every correlation carries source, age and confidence.
- Read-only functionality comes before any lifecycle write operation.

## 10. Required ADRs

### ADR 0018 — Reduce Diagnostics to Device Health and introduce Client 360

Records:

- retained and removed checks;
- continued local/remote support;
- Health naming;
- continued Event Log support;
- Network Scan separation;
- Client 360 overview behavior;
- preservation of `diagnostics_runs`.

### ADR 0019 — AD-authoritative User Management

Records:

- new `Wec.Modules.UserManagement`;
- stable AD identity keys;
- Core read-provider boundaries;
- no parallel employee master-data truth;
- read-only first phase;
- old Employee Lifecycle data retention.

### ADR 0020 — Computed Action Center read model

Records the non-persisted work-list decision and cross-module read projections.

### Later ADR — Controlled lifecycle actions

Required before AD account creation, disable, group mutation, OU movement or
deletion. It must cover roles, delegated rights, preview, confirmation, audit,
partial failure and rollback.

The frontend Relationship Map itself does not need an ADR as long as it remains
a presentation of existing read models and does not become an architectural
graph platform.

## 11. Verification gates

Every implementation phase ends with:

```powershell
git diff --check
dotnet build --configuration Release
dotnet test --configuration Release
dotnet run --project tools/Wec.ContractGenerator --configuration Release -- --check

Set-Location frontend
npm ci
npm test -- --run
npm run build
npm audit --audit-level=high
```

Additionally verify:

- production project references still satisfy the modular-monolith rules;
- local Desktop startup and direct route navigation;
- remote and local execution of all four retained Health checks;
- detailed Event Log queries;
- Client 360 with complete, partial and unavailable source data;
- User 360 against a representative AD lab, including large-directory paging;
- extended user/client evidence with built-in and service-profile filtering;
- keyboard and screen-reader operation of both map and list presentation;
- no automatic remote probe caused merely by opening a profile;
- no credentials or personal-data payloads in logs;
- GitHub CI, self-contained publish, host smoke, ZIP and installer.

## 12. Confirmed preflight decisions and execution boundaries

`ROADMAP_DECISIONS.md` is the authoritative compact decision register. The
confirmed product and privacy direction is:

- the visible workspace is named **Health**;
- historical Health data is retained while removed checks are hidden from the
  current view;
- AD is the authoritative user source for the MVP;
- extended interactive-user and local-profile evidence may be collected during
  explicit Inventory scans;
- observed user/client evidence never becomes an ownership claim;
- the first read-only lifecycle workflow is the **Leaver review**;
- old Employee Lifecycle tables remain preserved;
- multi-host read-only scans move into Clients before legacy wrappers are
  removed;
- the internal component is a `RelationshipMap`, presented as latest-known
  evidence rather than real-time telemetry;
- Action Center remains a computed read-only work list;
- no AD or automatic remediation writes are authorized.

The confirmed operating policy for a later explicitly started implementation
is:

- existing AD, Kaspersky, opsi and Nessus configurations may be used for
  bounded read-only smoke tests;
- while outside the company environment, tests use fixtures, mocks, the local
  machine and CI; provider and remote-client smoke tests remain explicit
  release gates;
- current WEC GitHub Actions artifacts may be deleted to resolve storage quota,
  but GitHub Release assets must be preserved;
- use small local commits on a `codex/...` branch;
- push and update a Draft PR only at major, fully verified phase milestones;
- clean only temporary branches, worktrees and generated artifacts created by
  the implementation, never unrelated user files or branches;
- a green pull request may be merged without another approval;
- tags, installer publication and GitHub Releases always require explicit user
  approval.

## 13. Final target state

The resulting product is not merely a collection of Windows tools. It becomes
an evidence-oriented administration workbench:

- Client 360 explains the complete operational state of a device;
- User 360 explains identity, access and reliably linked endpoints;
- Relationship Maps make source and confidence visible;
- Action Center turns findings into a prioritized read-only work list;
- guided assistants support decisions without premature automation;
- every detail remains traceable to a source, timestamp and focused module.

This target adds substantial value without abandoning the current desktop,
single-process modular-monolith architecture.
