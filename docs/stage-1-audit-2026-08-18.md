# Stage 1 Audit — Windows Enterprise Companion

Date: 2026-08-18

Scope: Inspection, execution, analysis, and planning only

Repository changes during audit: None

## Validation performed

- Release build completed successfully with zero compiler warnings.
- Backend test suite: 133/133 tests passed.
- Frontend test suite: 21/21 tests passed.
- Frontend production build completed successfully.
- All five application screens were inspected in the running Release application.
- Inventory refresh, local diagnostics, and Active Directory workgroup states were exercised.
- No production code, configuration, UI, or tests were modified during Stage 1.

## 1. Executive assessment

| Area | Rating | Assessment |
| --- | ---: | --- |
| Architecture | 8/10 | Project dependency rules, the modular monolith, typed bridge, `Result<T>`, and infrastructure boundaries are sound. Most problems are inside individual features rather than in the solution structure. |
| Maintainability | 7/10 | There are no major God objects or unnecessary layers. Parallel status semantics, duplicated system queries, and manually synchronized contracts create drift risk. |
| Readability | 8/10 | Classes are generally small and cohesive. Larger files such as `Program` and `DirectoryHygieneService` remain understandable and should not be split based on size alone. |
| Consistency | 6/10 | Error, timeout, not-run, shell-action, and status handling differ between features. |
| Testability | 7/10 | Backend seams and unit tests are strong. Frontend coverage and realistic CIM, network, LDAP, and startup-failure tests are incomplete. |
| Developer onboarding | 6/10 | The code can be mapped reasonably quickly, but binding documentation describes an older product state. |
| Product structure | 7/10 | The five modules have useful boundaries. Reporting is imprecisely named and some overlaps are not explained in the UI. |
| UX clarity | 6/10 | Navigation and visual language are consistent, but status meaning and screen purpose are sometimes ambiguous. |
| Visual hierarchy | 5/10 | Raw data and successful states often receive more prominence than actionable warnings. |
| Operational usefulness | 5/10 | Inventory is useful, but Security and Diagnostics can currently present false or poorly prioritized conclusions. |

Overall conclusion: a broad architectural rewrite is not justified. Correctness and status truth must be addressed first, followed by focused product-semantic and information-hierarchy improvements.

## 2. Current architecture map

```text
React/TypeScript feature page
        |
        v
bridgeClient.invoke(module, action, payload)
        |
        v
WebView2 message bridge
        |
        v
ActionDispatcher + request scope + correlation logging
        |
        v
IActionHandler
        |
        v
Module service / check / diagnostic
        |
        +-- Core abstraction -> WMI / Registry / LDAP / Network / Shell
        +-- Module repository -> EF Core / SQLite
        +-- Result<T> / typed DTO
        |
        v
Bridge response -> React page-local state -> UI
```

Responsibilities:

- `Wec.Host`: composition root, WebView2 shell, dependency injection, migrations, and bridge hosting.
- `Wec.Core`: contracts, results, errors, and infrastructure seams.
- `Wec.Infrastructure`: Windows providers, EF Core/SQLite, logging, and privilege context.
- `Wec.Modules.Inventory`: local hardware snapshot and cache.
- `Wec.Modules.Security`: persisted local security scans and history.
- `Wec.Modules.Diagnostics`: non-persisted live diagnostics.
- `Wec.Modules.ActiveDirectory`: LDAP-based directory overview and hygiene analysis.
- `Wec.Modules.Reporting`: reads existing Inventory and Security snapshots and exports HTML/JSON.
- Frontend: feature-local state without a global state framework.

## 3. What is already good

The following areas should not be rewritten without a concrete requirement:

- Project dependency rules are respected. Modules do not reference Infrastructure or other modules.
- WinForms with WebView2 remains appropriate for this local Windows application.
- The typed JSON bridge, correlation handling, and central dispatcher are understandable and tested.
- `Result<T>` and typed errors prevent internal exception details from leaking to the UI.
- WMI, LDAP, Registry, network, clock, repository, and shell seams are concrete test boundaries rather than abstract layering for its own sake.
- One `WecDbContext` with module entity configurations is simpler than separate module databases.
- ADR 0004 reporting read contracts correctly avoid cross-module project references.
- Diagnostics being live-only is a good product decision. Reporting should not silently execute diagnostics.
- Explicit `RequiresElevation` states are preferable to automatic elevation.
- Inventory uses suitable tables and definition lists rather than decorative charts.
- Security findings already provide useful evidence and recommendations.
- The main navigation is clear and visually consistent.
- No current need exists for MediatR, CQRS, an event bus, generic repositories, or additional application layers.
- `Program.cs` and `DirectoryHygieneService` are not God objects merely because of their line counts.

## 4. Critical findings

### 4.1 Security history can falsely mark unresolved findings as resolved

**Severity: Critical**

**Evidence:** `ScanHistoryService` compares only `(FindingId, AffectedResource)`. A previous finding missing from the next scan is considered resolved, regardless of whether its check executed successfully. Expected check failures instead create separate `*-NOT-RUN` findings, while unexpected check crashes may produce no finding at all.

**Why it matters:** A still-existing security problem can appear as resolved after a WMI, privilege, or check failure. The UI and HTML report can additionally state that all checks passed when checks failed.

**Recommended direction:** Immediately suppress resolved claims when the current scan is degraded or contains unexecuted checks. Persist a per-check execution outcome and only resolve a finding after the same check executes successfully.

### 4.2 Local Administrators check reports zero members incorrectly

**Severity: Critical**

**Evidence:** The real WMI query returned two memberships. `CimInstance.ToString()` produced values in the form `Win32_UserAccount (Name = "...", Domain = "...")`, while the parser expects a legacy `Domain="...",Name="..."` path. Unparseable values are discarded and the parsed count is displayed as the member count. Tests cover only the expected legacy string.

**Why it matters:** A security-sensitive group is shown as empty despite having members. This is false assurance rather than a cosmetic defect.

**Recommended direction:** Read membership data structurally from CIM or robustly support actual representations. Track raw results, parsed members, and parse failures separately. Never confirm zero members after parse failures. Add a test using the observed CIM representation.

### 4.3 Security conflates scan completion, coverage, and risk

**Severity: High**

**Evidence:** The running application displayed a green `COMPLETED` status while BitLocker and TPM checks were not executed because elevation was required. Not-run checks are modeled as informational findings. `CompletedWithErrors` is reserved for unexpected exceptions.

**Why it matters:** “The scan process ended,” “all checks ran,” and “no security problem exists” are different statements. The current presentation makes them appear equivalent.

**Recommended direction:** Introduce a small `SecurityCheckResult` containing `CheckId`, execution status, findings, and sanitized failure information. Persist coverage so it remains available after reload and in reports. `FindingSeverity` should describe only the severity of an observed condition.

### 4.4 OS lifecycle evaluation ignores Enterprise and LTSC editions

**Severity: High**

**Evidence:** `OsSupportCheck` contains a Home/Pro lifecycle snapshot, queries only caption and build number, and applies the result to every edition.

**Why it matters:** Supported Enterprise or LTSC systems can be reported as end-of-life. Microsoft publishes different lifecycle dates for Windows 11 Home/Pro, Enterprise/Education, and Windows 10 Enterprise LTSC.

**Recommended direction:** Capture a reliable edition/SKU and servicing channel. Emit an EOL finding only for an unambiguous edition/build match; report unknown combinations as unknown. Keep an offline table, but give it edition-aware tests and a documented update process.

Official lifecycle references:

- <https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information>
- <https://learn.microsoft.com/en-us/lifecycle/products/windows-11-enterprise-and-education>
- <https://learn.microsoft.com/en-us/lifecycle/products/windows-10-enterprise-ltsc-2021>

### 4.5 Time synchronization can turn provider failures into PASS or a crash

**Severity: High**

**Evidence:** The second Registry result is accessed without checking whether it succeeded. WMI failures are reduced to `unknown` and can still lead to a PASS result.

**Why it matters:** Expected Registry or WMI failures can appear as either an internal exception or healthy time synchronization.

**Recommended direction:** Evaluate every provider result explicitly. Return `NotRun` or a clearly named warning when information is incomplete. Add tests for the missing Registry and WMI error paths.

### 4.6 Diagnostics prioritizes irrelevant and potentially wrong network data

**Severity: High**

**Evidence:** A real diagnostics run displayed 32 “active adapters,” including Npcap, WFP, QoS, and virtual filter interfaces. The resulting large PASS card pushed an Event Log warning several scroll heights down. Gateway reachability selected the first gateway, an IPv6 link-local value, despite an available IPv4 gateway.

**Why it matters:** The administrator does not see the relevant route or most urgent condition first. Gateway reachability can test a path that does not represent normal traffic.

**Recommended direction:** Identify meaningful IP-capable interfaces and the preferred route. Sort results `FAIL -> WARNING -> NOT_RUN -> PASS`. Collapse successful raw evidence by default and place filter interfaces under secondary details.

### 4.7 Active Directory searches materialize entire directories despite paging

**Severity: High**

**Evidence:** `LdapDirectoryReader` collects all pages into a list. Overview operations count the resulting list, and hygiene operations apply `Take(ExampleLimit)` only after full materialization.

**Why it matters:** Large directories can create substantial memory use and long runtimes. Server paging avoids the LDAP page-size limit but not client-side materialization.

**Recommended direction:** Extend the concrete directory seam with a bounded search result containing an exact count and at most a requested number of example entries. Do not introduce a generic streaming framework.

### 4.8 Frontend timeouts and backend operations have different lifetimes

**Severity: High**

**Evidence:** The bridge client defaults to ten seconds, while some later features use ad hoc 120-second values. The host dispatches with `CancellationToken.None`, and synchronous WMI/LDAP calls can continue after the frontend gives up.

**Why it matters:** The UI can report a timeout while the operation continues. Repeated user actions can create overlapping provider work.

**Recommended direction:** Define central action-specific timeouts and bound provider operations. Do not build a full cancellation protocol until interactive cancellation is a confirmed requirement.

### 4.9 Development checkouts share database and log paths

**Severity: High**

**Evidence:** Defaults point to `%LOCALAPPDATA%\Wec\wec.db` and `%LOCALAPPDATA%\Wec\logs`. The runtime log contained starts and module lists from a different checkout alongside the current run.

**Why it matters:** Checkouts or installed versions can mix logs, data, and migrations, creating diagnostic and schema risks.

**Recommended direction:** Add configurable development or instance profiles with separate database, log, and WebView profile paths. Keep these paths in options rather than distributed hardcoded values.

### 4.10 Binding documentation describes an older product

**Severity: High**

**Evidence:** Agent instructions still identify M1 as the current milestone and prohibit later work. The README says no Active Directory functionality exists and lists only Inventory, while five modules are registered by the application.

**Why it matters:** New developers and coding agents receive contradictory instructions marked as binding.

**Recommended direction:** Mark the foundation/M1 plan as historical, maintain one current product overview, add the missing module READMEs, and keep one canonical agent instruction source.

## 5. Over-engineering findings

- `ModuleDescriptor` is produced by every module but is not used for navigation or discovery. Do not build dynamic navigation to justify it; define its purpose or remove it later.
- `CheckStatus` is unused while Security, Diagnostics, and the UI use parallel semantics. Do not add another status model. Either use it for Security execution or remove it together with the binding documentation decision.
- The Security finding-count sparkline adds visual surface without supporting a reliable decision.
- A universal system-fact provider, event bus, or query bus would be disproportionate for the current small WMI duplications.
- `HandlerRegistration` reflection is small, local, and tested. Replacing it with a framework would not improve the product.
- Generic base classes for all checks or diagnostics would make the existing small implementations harder to understand.
- A top-level dashboard would currently duplicate single-machine information.

## 6. Under-engineering findings

- Persisted Security check outcomes and coverage.
- Realistic provider contract tests for CIM and network representations.
- Bounded Active Directory materialization.
- Edition-aware OS lifecycle modeling.
- Consistent action-specific timeouts.
- A startup error boundary for migration, SQLite/path, and WebView initialization failures.
- Semantic validation of positive durations and non-empty option lists.
- A distinction between administrator summaries and expandable raw evidence.
- Separate development/installation state profiles.
- Current canonical project documentation.
- A small C# to TypeScript contract generator; `api-types.ts` has grown beyond the project’s own stated manual-sync threshold.

## 7. Rapid-development artifacts

These findings are based on observable inconsistency, not on assumptions about authorship:

- Security uses `INFO` for information, unknown state, and an unexecuted check.
- `FirewallProfilesCheck` has its own not-run helper while later checks use `CheckFindings.NotRun`.
- Several 120-second UI timeouts are inline while similar actions retain the ten-second default.
- Reporting uses `IShellLauncher`; `OpenLogsFolderHandler` duplicates `Process.Start`; frontend callers silently discard errors.
- Backend modules expose `ModuleDescriptor` while frontend navigation is fully static.
- Raw evidence dictionaries are rendered uniformly despite very different operational meaning.
- Inventory and Security duplicate BitLocker privilege, query, and status logic.
- Documentation, milestone plans, and implemented modules have drifted apart.
- Unknown persisted enum values are silently converted into plausible but potentially incorrect domain values.
- Expected EF configuration-scanning warnings are logged on every startup, reducing log signal quality.

## 8. Feature and tab overlap matrix

| Area A | Area B | Overlap | Recommendation |
| --- | --- | ---: | --- |
| Inventory | Security | Medium | Keep separate: Inventory presents observed state; Security evaluates risk. Consolidate BitLocker acquisition when next touched. |
| Security | Diagnostics | Low | Keep separate: persisted posture versus live troubleshooting. Explain scope more clearly in the UI. |
| Diagnostics | Active Directory | Medium | Diagnostics owns local domain membership; AD owns directory content and hygiene. Link contextually rather than merge. |
| Active Directory | Security | Low | Directory hygiene remains in AD; local endpoint posture remains in Security. |
| Reporting | Inventory | High | Intentional consumer relationship. Show snapshot age and provenance before export. |
| Reporting | Security | High | Include coverage, age, and highest severity in export readiness. |
| Reporting | Diagnostics | Low | Keep Diagnostics live-only and explicitly excluded. |
| Reporting | Active Directory | Medium | State the exclusion; include AD only after a defined snapshot or persistence requirement. |
| Dashboard | All areas | Low today | Do not add a dashboard. Improve action-oriented summaries within existing screens first. |

No current module boundary justifies merging projects or tabs.

## 9. UX findings

Prioritized by operational value:

1. Security must count issues, unknown/not-run states, and information separately.
2. Diagnostics must prioritize attention rather than backend registration order.
3. Reporting needs a readiness block showing age, missing sources, and incomplete Security coverage.
4. Active Directory needs a single workgroup preflight; hygiene should be disabled after a known not-applicable result.
5. The Reporting screen is functionally report export. The UI label should set that expectation without renaming the backend project.
6. Security severity filters need toggle semantics, counts, and correctly associated labels.
7. Inventory refresh should leave the previous snapshot visible.
8. The splash blocks a frequently used administration tool for about 2.35 seconds and should be removed or shortened.
9. The global sidebar footer gives database and log paths too much prominence and uses very small action targets.
10. Page-enter animations reduce immediate readability during frequent navigation without operational benefit.

The dark administration UI, navigation, focus states, and semantic base elements should remain.

## 10. Dashboard and visualization recommendations

### Useful immediately

| Recommendation | Operational decision supported |
| --- | --- |
| Security coverage summary | Can the scan be trusted, or must it be rerun or elevated? |
| Diagnostics status summary ordered by urgency | Which problem should be investigated first? |
| Report readiness with data age and missing sources | Must data be refreshed before export? |
| AD hygiene with non-zero results first | Which accounts or groups require review? |
| Inventory provenance, age, and compact totals | Is this the expected and current device state? |

### Useful later

| Recommendation | Required foundation |
| --- | --- |
| Security history by severity and coverage | Persisted check-set version, privilege context, and comparable coverage. |
| Hardware change list | Snapshot history. Prefer a diff over a chart. |
| Disk capacity warning | Logical volume and free-space data. |
| AD hygiene trend | Real-domain validation and persistence. |
| Software/OS staleness overview | Reliable software inventory and support data. |
| Fleet dashboard | Multiple endpoints or connected management systems. |

### Probably unnecessary

- A standalone dashboard in the current single-machine product.
- The finding-count sparkline.
- A weighted overall Security or health score.
- Pie/donut charts for a few status values.
- Charts for static CPU, RAM, or disk configuration.
- Large AD KPI cards for raw object counts.
- Patch, deployment, or unmanaged-endpoint metrics without corresponding data sources.

## 11. Prioritized technical-debt backlog

### P0 — before major feature development

- Correct the Local Administrators parser and test the real CIM representation.
- Prevent false resolved findings in Security history.
- Model Security check coverage and execution results.
- Make OS lifecycle evaluation edition/channel aware.
- Correct Time Synchronization error paths.
- Bring binding documentation up to the actual project state.

### P1 — high value

- Restrict Diagnostics to relevant adapters and a suitable gateway route.
- Order Diagnostics by urgency and collapse PASS evidence.
- Bound Active Directory result materialization.
- Centralize frontend/backend timeout semantics.
- Separate development and installed data/log/WebView profiles.
- Add data age, completeness, and Security coverage to Reporting.
- Add a startup error boundary for database, path, and WebView failures.
- Add realistic or captured Windows provider fixtures and a documented AD lab test.
- Add frontend tests for Inventory, Diagnostics, Active Directory, and footer states.

### P2 — structural consistency

- Add a C# to TypeScript contract generator with a CI diff check.
- Validate option values semantically.
- Separate raw evidence from administrator summaries.
- Normalize shell launches and UI error reporting.
- Remove expected EF scanning warnings.
- Align Reporting and Diagnostics copy with actual scope.
- Remove the Security sparkline.
- Consolidate BitLocker acquisition during the next related change.
- Extract recurring page-header/button styles only as part of an already planned UI change.

### P3 — optional or cosmetic

- Reduce the splash and page-enter animation.
- Improve footer target sizes and accessibility details.
- Remove or formally justify `ModuleDescriptor`.
- Normalize small naming and helper inconsistencies.
- Handle unknown persisted enum values visibly.

## 12. Proposed refactoring strategy

This strategy is a plan only and has not been implemented.

| Phase | Goal | Affected areas | Expected benefit | Risk | Verification |
| --- | --- | --- | --- | --- | --- |
| A — Correctness gates | Eliminate false Security and Diagnostics conclusions | Local Admin, Security history, OS lifecycle, Time Sync | Prevents false assurance | Medium; domain semantics change | Regression tests, real CIM fixture, full suite, manual Security run |
| B — Status and data truth | Separate completion, coverage, findings, and unknown states | Security domain, persistence, UI, reports | Reliable history and reporting | High; migration and contract changes | Migration tests, reload tests, UI/report cases, not-run/crash diff tests |
| C — Provider boundaries | Improve runtime, scalability, and instance isolation | LDAP, network, timeouts, paths, startup | Enterprise stability | Medium | Large LDAP fixtures, network fixtures, timeout tests, isolated profiles |
| D — Normalize concrete patterns | Remove only proven inconsistencies | Shell actions, options, TS contracts, optional BitLocker reader | Less drift without framework growth | Low to medium | Composition tests, contract diff, module unit tests |
| E — Product semantics | Clarify each screen’s purpose and boundary | Security, Diagnostics, AD, Reports | Faster orientation and fewer misinterpretations | Low | Task-based manual UX checks and component tests |
| F — Information hierarchy | Put actionable states first | Diagnostics evidence, Security summary, AD hygiene, report readiness | Higher operational value | Medium; risk of hiding detail | Real-run comparison with raw evidence still available |
| G — Visual polish | Improve interaction after functional stabilization | Splash, animations, footer, filter accessibility | Better daily usability | Low | Keyboard, 1280x830, High-DPI, and reduced-motion tests |

Each phase should be delivered as small, independently verifiable changes. Module reorganization should not be mixed with domain bug fixes.

## 13. Proposed minimal target structure

The project structure remains in place. No new projects or generic architectural layers are needed.

```text
Wec.Core
|-- existing Result/Error/bridge contracts
|-- CheckStatus as technical Security-check execution status
`-- optional small IDiskEncryptionStatusReader contract

Wec.Infrastructure
|-- existing Windows providers
|-- bounded LDAP search implementation
|-- more precise network/route selection
`-- optional DiskEncryptionStatusReader

Wec.Modules.Inventory
`-- maps raw state to inventory facts

Wec.Modules.Security
|-- SecurityCheckResult
|-- persisted check outcomes and coverage
|-- findings only for evaluated conditions
`-- coverage-aware history diff

Wec.Modules.Diagnostics
|-- keeps its DiagnosticStatus model
|-- remains live-only
`-- provides summary plus expandable raw evidence

Wec.Modules.ActiveDirectory
`-- owns bounded directory analysis

Wec.Modules.Reporting
`-- remains a consumer/exporter of existing snapshots with readiness metadata

frontend
|-- keeps the current feature structure
|-- adds small domain-specific summaries
`-- keeps page-local state
```

The optional BitLocker reader should be extracted only when one of the existing implementations is next changed. It does not justify a general system-facts framework.

## 14. Explicit recommendations not to change

- Do not replace WinForms/WebView2 with Electron, Tauri, or a local HTTP server.
- Do not merge the five modules.
- Do not add separate Domain/Application/Infrastructure projects per feature.
- Do not introduce cross-module feature references.
- Keep `Wec.Host` as the explicit composition root.
- Keep the typed bridge, `IActionHandler`, and `Result<T>`.
- Keep one `WecDbContext` and EF migrations.
- Keep ADR 0004 read contracts for Reporting.
- Do not add global React state management.
- Do not add MediatR, CQRS, an event bus, or generic repositories.
- Do not introduce generic base classes for checks or diagnostics.
- Do not persist Diagnostics solely to add content to Reporting.
- Do not build a dynamic plugin or navigation system.
- Do not add a dashboard while the product has only one local machine and few persisted sources.
- Do not merge Inventory with Security.
- Do not merge Diagnostics with Active Directory.
- Do not split `Program.cs` or `DirectoryHygieneService` based on line count alone.
- Do not remove existing evidence and recommendations; change their visual priority instead.
- Do not replace effective hardware tables with charts.

## Stage 1 stop gate

No production changes are authorized by this report. Implementation begins only after explicit approval of Stage 2 scope.

**STAGE 1 COMPLETE — Awaiting review and approval before any repository modifications.**
