# Windows Enterprise Companion — Roadmap Decisions

Status: confirmed decisions applied to the roadmap implementation

Date: 2026-08-26

Implementation application verified: 2026-08-27

This file is the authoritative compact decision register for the completed
`docs/archive/ultimate-admin-tool-roadmap.md`. Repository architecture documents and
accepted ADRs remain binding. If a later implementation conflicts with either,
work must stop at the conflict instead of silently changing the decision.

## D-001 — Extended client user evidence is approved

Decision: approved with privacy limits.

WEC may collect the following evidence during an explicitly started Inventory
scan:

- the interactive domain user observed on the client;
- local user-profile SIDs;
- resolved domain identity when available;
- profile-presence evidence;
- available profile last-use timestamps;
- the source and observation time.

The evidence may be persisted in the latest Inventory projection so User 360
can relate identities to clients. It must not be represented as authoritative
device ownership.

Required limits:

- filter built-in, system and service profiles through explicit tested rules;
- do not collect profile contents, documents or arbitrary registry data;
- do not build a separate user-activity history;
- superseded observations follow the Inventory retention policy;
- every relationship exposes source, age, relationship type and confidence;
- host naming conventions are never used to infer ownership.

The user selected the extended option rather than the more conservative
interactive-user-only recommendation.

## D-002 — Leaver is the first User Lifecycle workflow

Decision: confirmed recommendation.

After the read-only User 360 foundation and user/client correlation, implement
a read-only Leaver review before Joiner or Mover workflows.

It includes:

- account enabled/disabled state and last activity;
- remaining direct and privileged groups;
- linked devices and device-return evidence;
- outstanding access and device context;
- source completeness and freshness;
- an exportable review checklist.

It does not disable an account, remove groups, move directory objects or mutate
devices. Controlled actions require a later ADR and explicit authorization.

## D-003 — Bounded read-only live integration tests are authorized

Decision: confirmed recommendation with a safe target boundary.

Existing configured AD, Kaspersky, opsi and Nessus connections may be used for
bounded read-only integration and smoke tests. Stored credentials remain in the
existing secure handling path and must never be printed, persisted elsewhere or
included in test output.

Until a concrete remote test client is explicitly designated:

- provider-level read-only tests may use the configured systems;
- per-host Inventory, Health, Event Log, Ping and WinRM smoke tests are limited
  to the local machine;
- automated fixtures and mocks cover remote-host behavior;
- absence of a named remote client does not block unrelated implementation
  phases, but true remote acceptance remains required before release.

No write endpoint or mutation is authorized by this decision.

## D-004 — GitHub Actions artifacts may be cleaned up

Decision: confirmed recommendation.

When implementation is explicitly started, current Actions artifacts for this
repository may be deleted if required to restore artifact uploads or control
storage usage.

Boundaries:

- GitHub Release assets must not be deleted;
- published tags and releases must not be altered by cleanup;
- prefer removing obsolete or superseded CI artifacts;
- report what was removed and note that Actions-artifact deletion is not
  recoverable;
- apply the reduced retention policy so the quota does not immediately fill
  again.

## D-005 — Git work uses milestone pushes

Decision: confirmed recommendation with reduced push frequency.

After an explicit implementation start:

- create a `codex/...` implementation branch;
- make small, reviewable local commits per slice;
- keep each commit buildable and testable in proportion to its scope;
- push only at large phase milestones after the full milestone gates pass;
- create or update a Draft PR at those milestones, not after every commit;
- stage only files belonging to the current slice;
- preserve unrelated user changes and untracked files.

Cleanup is authorized only for temporary branches, worktrees, generated files
and other artifacts created by the implementation when they are no longer
needed. Do not delete or rewrite unrelated branches, worktrees, files or user
changes. Never use destructive repository resets.

## D-006 — Merge is autonomous; release is approval-gated

Decision: user selected merge autonomy instead of the recommended stop-before-
merge boundary.

After all required checks pass, Codex may merge the green implementation pull
request without another approval.

Codex must stop and request explicit approval before any of the following:

- creating or pushing a version tag;
- publishing an installer;
- creating or publishing a GitHub Release;
- altering an existing published release.

A merge is allowed only when the branch is current with the target branch, the
required CI is green, review findings are resolved and no mandatory merge gate
is outstanding. Release-only company-environment gates from D-008 may remain
open and must be reported explicitly.

## D-007 — Multi-host scans move into the Clients workspace

Decision: confirmed recommendation.

Preserve the useful read-only multi-host Inventory, Security and Health scan
capabilities from ADR 0010, but move them into the canonical Clients workspace.
After feature parity is verified, remove the unrouted standalone page wrappers
and their no-longer-used target-selection shell.

The integrated workflow must provide:

- explicit selection of a bounded client set;
- a visible operation summary before execution;
- per-host progress, cancellation and typed failures;
- no implicit scan caused by selection or navigation;
- the same credential and secret-handling rules as individual client scans;
- no automatic remediation or remote write.

Global Reporting remains a separate workspace. This decision changes the UI
placement described by ADR 0010 but preserves its deliberate batch capability;
the roadmap ADR must record that narrow supersession.

## D-008 — Autonomous development may run without the company environment

Decision: proceed without live company systems because the current host is
outside the company environment.

The autonomous implementation may use:

- automated unit, integration and frontend tests;
- fixtures and mocked provider seams;
- the local machine for Desktop, Inventory, Health and Event Log smoke tests;
- GitHub CI for publish, host smoke, ZIP and installer verification.

Unavailable AD, Kaspersky, opsi, Nessus and remote-client live tests do not
block implementation phases or an otherwise green merge. They remain explicit
release-acceptance gates and must not be reported as passed.

When the company environment is available again, configure providers through
WEC and designate a non-critical WinRM client. Do not place endpoints,
usernames, passwords or other company secrets in this file, source control or
test output.

## Confirmed roadmap defaults

The following roadmap recommendations are also confirmed and require no further
product decision during the planned implementation:

- keep the desktop WinForms/WebView2 modular monolith;
- name the reduced visible Diagnostics workspace **Health**;
- retain local and remote Windows Update age, selected service-state, Event Log
  summary and free-disk-space checks;
- retain detailed Event Log queries;
- keep Network Scan separate and unchanged by the Health reduction;
- preserve historical `diagnostics_runs` data and old Employee Lifecycle
  tables;
- preserve multi-host read-only scan capability by integrating it into Clients
  before removing standalone wrappers;
- use Active Directory as the authoritative User Management identity source;
- create `Wec.Modules.UserManagement` rather than reviving Employee CRUD as a
  second identity truth;
- keep User Management, Action Center, cleanup and Leaver workflows read-only
  in their first versions;
- use a computed Action Center rather than persisted work items;
- use an evidence-aware bounded Relationship Map rather than an unrestricted
  environment graph;
- perform route-based lazy loading before adding the large new workspaces;
- do not introduce server mode, plugins, microservices, CQRS/MediatR, an event
  bus, arbitrary PowerShell, AI features or automatic remediation;
- code signing remains a separate later topic.

## Execution and release boundary

The completed roadmap was merged into `master` as `e89395f` on 2026-08-27.
The decisions above remain authoritative for the resulting product and for any
successor work unless a later decision or accepted ADR supersedes them.

The unavailable company environment and absence of a named remote test client
remain release-acceptance dependencies. A version tag, installer publication
or GitHub Release remains blocked until the required provider and remote-client
validation has run in the company environment.

Any newly discovered requirement involving personal-data expansion, directory
writes, credentials, new external systems, destructive data migration or a
conflict with an accepted ADR remains a stop condition and requires a new user
decision.
