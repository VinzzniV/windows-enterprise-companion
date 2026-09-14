# Microsoft 365 implementation

Local implementation and automated verification are complete. Live-tenant
authentication/permission acceptance and published-host packaging remain open.
The final update of `ROADMAP_EXECUTION.md` is blocked by another process's
Windows file lock; its active-slice header is therefore older than this report.

## Repository analysis and plan (2026-09-14)

Baseline: `228b719`, clean `codex/device-cleanup-excel-export` checkout.
Implementation continues on `codex/microsoft-365-read-only`; existing commits
are preserved. The previous roadmap is complete. This is the user's explicitly
requested next product slice.

### Existing architecture

- External sources use Core seams: `IDirectoryReader` (LDAP), `IOpsiClient`
  (JSON-RPC), concrete AD/opsi/Nessus computer projections and stored Inventory,
  Health and Security projections. Some older integrations keep transport in
  feature modules; this is not a reason to expose Graph to React or modules.
- `Program.BuildHost` is the composition root. `IModule.RegisterServices`
  registers application services and scoped `IActionHandler` implementations.
  Infrastructure implements Core seams. Modules reference only Core.
- Domain records and purpose-built result DTOs remain separate from EF entities.
  Repositories exist where persistence is required, not for every feature.
  TypeScript contracts are generated from bridge handler roots. React state,
  hooks and presentation models serve the role of view models; there is no WPF
  MVVM framework to extend.
- Non-secret settings use validated options and user-settings overrides.
  Kaspersky/opsi/Nessus credentials use Windows Credential Manager. Windows
  admin credentials are session-only. Neither path should store OAuth tokens.
- The bridge provides correlated request cancellation and action timeouts.
  Shared UI has Button, Card, Input, DataTable paging, Spinner and error states.
  A central route registry provides lazy loading and navigation search.
- Users are AD-authoritative by objectGUID with SID evidence. Client 360
  composes stored evidence and explicitly requested management-source context.
  No existing Entra, Intune, Exchange or cloud licensing integration exists.
  Local group/security evidence does not establish cloud groups, MFA, cloud
  compliance or mailbox existence.

### Reuse and technical debt

Reuse typed Result errors, Core read projections, generated DTOs, existing
components, source timestamps and lazy routes. Do not extend the large legacy
`ItHygieneService` with another transport. Do not reuse `viewCache` for cloud
personal data: its localStorage persistence and connection-key semantics are
unsuitable. Existing host/frontend timeout tables must both include new long
actions. Existing device identity is host-oriented and lacks Entra/sourceAnchor
evidence: do not invent reliable joins from host names. Scope grants cannot be
used as a substitute for endpoint/role/license checks.

### Implementation sequence

1. ADR 0021, explicit decision register and active execution slice.
2. Concrete Core models, Graph SDK/MSAL WAM implementation, options, bounded
   GET-only transport and typed sanitized errors.
3. Module session cache, connection/read handlers, mapping, paging and tests.
4. Tenant, user, license, group, Entra, optional Intune and report UI using
   existing controls; no persistent cloud inventory.
5. Evidence-aware correlation panels in User 360 and Client 360.
6. Full backend/frontend/contracts/build/audit gates, security review and
   deployment/permissions documentation. Local commits at green slice gates.

Each major implementation step runs the affected existing tests; the final
gate runs the complete suite. All Graph tests use a fake transport or Core seam.

## Authentication and deployment

Use an organizational, single-tenant public-client app registration. Configure
Mobile and desktop applications redirect
`ms-appx-web://microsoft.aad.brokerplugin/{client-id}`. Enter tenant ID and client
ID in WEC; neither is a secret. Grant only the enabled delegated read scopes.
Save non-secret defaults under Settings > Microsoft 365; they are merged into
the existing user-settings file without replacing provider limits, cache options
or other modules. Restart WEC to apply saved defaults. The connection form can
override them for the current session. Saving settings never triggers Graph.
WAM is required. No system-browser/device-code fallback, app secret, certificate
private key, password flow, `.default` scope or application access is used.

MSAL silently renews tokens for the explicitly selected account. UI-required
authentication is surfaced for another explicit sign-in. Disconnect clears
WEC's session; it does not remove the Windows account, revoke consent or promise
token revocation. Role assignment, Conditional Access, Intune licensing and
report licensing must be validated in the target tenant.

References: [WAM](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/desktop-mobile/wam),
[browser redirect behavior](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/using-web-browsers).

## Implemented Graph endpoints and permissions

All endpoints below use `https://graph.microsoft.com/v1.0`. WEC implements
**delegated organizational-account access only**. The table describes the
permissions actually requested/checked for the selected projection, including
permission to read the related object type. No application mode is implemented.
Consent values are from the [Microsoft permission reference](https://learn.microsoft.com/en-us/graph/permissions-reference).

| Function | Graph endpoint | Permission | Delegated/Application | Admin consent |
| --- | --- | --- | --- | --- |
| Tenant name and ID | `GET /organization?$select=id,displayName` | `User.Read` | Delegated | No (tenant policy can restrict user consent) |
| Entra users, administrative fields and assigned SKU IDs | `GET /users`, `GET /users/{id}` with explicit `$select` | `User.Read.All` | Delegated | Yes |
| Groups, type and membership-rule fields | `GET /groups`, `GET /groups/{id}` with explicit `$select` | `GroupMember.Read.All` | Delegated | Yes |
| Another user's direct groups | `GET /users/{id}/memberOf/microsoft.graph.group` | `User.Read.All` + `GroupMember.Read.All` for group data | Delegated | Yes |
| Direct group members | `GET /groups/{id}/members` | `GroupMember.Read.All`; existing `User.Read.All` / `Device.Read.All` provide related type information | Delegated | Yes |
| Entra devices | `GET /devices`, `GET /devices/{id}` | `Device.Read.All` | Delegated | Yes |
| Another user's registered devices | `GET /users/{id}/registeredDevices/microsoft.graph.device` | `User.Read.All` + `Device.Read.All` for device properties | Delegated | Yes |
| Registered device owners | `GET /devices/{id}/registeredOwners` | `Device.Read.All`; existing `User.Read.All` provides user properties | Delegated | Yes |
| Tenant SKUs and capacity | `GET /subscribedSkus` | `LicenseAssignment.Read.All` | Delegated | Yes |
| User license details and service plans | `GET /users/{id}/licenseDetails` | `LicenseAssignment.Read.All` (already selected `User.Read.All` is also accepted by Graph) | Delegated | Yes |
| Intune managed devices (opt-in) | `GET /deviceManagement/managedDevices` | `DeviceManagementManagedDevices.Read.All` | Delegated | Yes |
| Last sign-in / successful sign-in (opt-in) | `GET /users/{id}?$select=id,signInActivity` | `User.Read.All` + `AuditLog.Read.All` | Delegated | Yes |
| MFA registration/capability (opt-in) | `GET /reports/authenticationMethods/userRegistrationDetails/{id}` | `AuditLog.Read.All` | Delegated | Yes |

Endpoint references:
[organization](https://learn.microsoft.com/en-us/graph/api/organization-list?view=graph-rest-1.0),
[users](https://learn.microsoft.com/en-us/graph/api/user-list?view=graph-rest-1.0),
[groups](https://learn.microsoft.com/en-us/graph/api/group-get?view=graph-rest-1.0),
[user membership](https://learn.microsoft.com/en-us/graph/api/user-list-memberof?view=graph-rest-1.0),
[group members](https://learn.microsoft.com/en-us/graph/api/group-list-members?view=graph-rest-1.0),
[registered devices](https://learn.microsoft.com/en-us/graph/api/user-list-registereddevices?view=graph-rest-1.0),
[registered owners](https://learn.microsoft.com/en-us/graph/api/device-list-registeredowners?view=graph-rest-1.0),
[SKUs](https://learn.microsoft.com/en-us/graph/api/subscribedsku-list?view=graph-rest-1.0),
[user license details](https://learn.microsoft.com/en-us/graph/api/user-list-licensedetails?view=graph-rest-1.0),
[Intune](https://learn.microsoft.com/en-us/graph/api/intune-devices-manageddevice-list?view=graph-rest-1.0),
[registration report](https://learn.microsoft.com/en-us/graph/api/userregistrationdetails-get?view=graph-rest-1.0).

`User.ReadBasic.All` cannot supply the requested administrative user fields.
`User.Read` suffices for the selected tenant properties, so
`Organization.Read.All` is unnecessary. `GroupMember.Read.All` is selected over
`Group.Read.All`, which also grants access to group conversations. Group
properties unavailable under the selected permission remain null; WEC does
not automatically escalate. `Member.Read.Hidden` and application/service-
principal inventory permissions are not requested.

`AuditLog.Read.All` is powerful and therefore opt-in. The implementation reads
only per-user sign-in timestamps and authentication-registration categories;
it does not query sign-in events, authentication method secrets, policies or
audit-log histories. Reports require suitable Entra roles (for example Reports
Reader or Security Reader) and applicable licensing. Intune requires an active
Intune license and the signed-in user's effective Intune/RBAC access.

Some Microsoft troubleshooting guidance describes intermittent report license
checks requiring `Directory.Read.All`. WEC deliberately does not broaden the
profile as a workaround; affected tenants receive an explicit access/query
failure, and acceptance must establish whether the narrow profile works.
See [Microsoft's report license-check guidance](https://learn.microsoft.com/en-us/troubleshoot/entra/entra-id/users-groups-entra-apis/b2c-or-tenant-premium-license-sign-in-activities).

## Cache and synchronization

- The singleton module service owns up to 32 query snapshots, isolated by the
  current connection. Freshness is ten minutes; retention is one hour. These
  are validated options, including a configurable 90% license warning ratio.
  Expired entries are evicted on the next module access; idle-process memory
  and already displayed React data are not presented as a secure-erasure store.
- Navigation reuses retained data, including stale snapshots with an explicit
  label. Refresh is manual. Initial reads load a selected collection, not the
  whole tenant. There is no scheduler, background scan or durable cloud cache.
- A cancellable gate serializes Graph work and coalesces equivalent successful
  reads/refreshes. Cancellation publishes no partial snapshot. Disconnect
  cancels active reads, invalidates waiting old-session reads and clears cache.
- Each collection uses Graph continuation links, at most 100 records per
  requested page, 20 pages and 2,000 retained records by default. Each response
  is bounded to 4 MiB. Limits are configurable and truncation stays visible.
- Total query time defaults to 60 seconds, including retries and paging.
  Authentication defaults to 180 seconds. Host/frontend deadlines are longer
  than the provider's validated maximums. All waits accept cancellation.
- 429/502/503/504 can retry twice. `Retry-After` is respected; a wait over the
  configured 15-second maximum returns a typed failure instead of retrying
  earlier than requested. Without the header, bounded exponential delay applies.
- Failed refresh preserves the previous timestamp and data with an error.
  Failed first reads remain errors and appear in the overview with unknown
  counts. Source timestamps describe retrieval, not the age of individual
  source facts; Intune sync and Entra sign-in timestamps remain separate fields.
- Frontend search/sort operates across the entire **loaded** collection and
  renders 50-row pages using the shared table. It never claims tenant-wide
  search when the collection is truncated.

## Security review

| Boundary | Implementation and remaining risk |
| --- | --- |
| Least privilege | Fixed delegated read-scope allowlist; optional Intune/reports. Unexpected token scopes are rejected, including prior consent to write scopes. |
| Credential/token storage | MSAL in-memory cache and OS-protected WAM storage only. No custom token lifecycle, serialization, SQLite secret, localStorage data or bridge token. |
| Read-only guarantee | Public Core seam and bridge expose concrete read resources. An HTTP handler rejects every method except GET. Graph write methods are never exposed. |
| Outbound destinations | Fixed Microsoft public-cloud authority and HTTPS Graph origin. Continuations must stay on the original collection path. Cycles, foreign hosts, userinfo and alternate ports are rejected. Redirects and cookies are disabled. |
| UI trust | Bridge messages must originate from the configured frontend origin; navigation and new windows cannot substitute arbitrary remote content. WAM uses the native window handle. |
| Logs | Record resource enum, typed error and HTTP status. Do not log SDK exception objects, tokens, raw bodies, account names, SIDs or query URLs. No source data in execution notes. |
| Privacy | Requested administrative fields are transient only. Mailbox contents, authentication secrets, sign-in histories and persistent cloud inventory are outside scope. |
| Compromised workstation | Read-only code cannot defend a compromised Windows session. The app registration must be dedicated, assignment restricted as appropriate, roles minimal and Conditional Access enforced by the tenant. |
| Dependencies | Graph 5.105.0/MSAL broker 4.89.0 are centrally pinned. Kiota.Abstractions 1.22.2 explicitly overrides Graph 5.x's vulnerable transitive baseline; redirects are disabled independently. |

The redirect dependency fix is documented in
[GHSA-7j59-v9qr-6fq9](https://github.com/advisories/GHSA-7j59-v9qr-6fq9).
No live consent grant, app registration creation, Graph write, remote scan,
release publication or credential extraction was performed during development.

## Component inventory

New backend components:

- Core: `Microsoft365Contracts.cs` with concrete source models, query enum,
  connection scope grants, `IMicrosoft365Reader` and authentication-window seam.
- Infrastructure: `Microsoft365Options`, `Microsoft365Scopes`,
  `MsalGraphSession`, `GraphReadOnlyHandler`, `GraphQueries`, `GraphMapping`,
  `Microsoft365Errors`, `MicrosoftGraphReader`.
- Module: `Microsoft365Module`, `Microsoft365CacheOptions`,
  `Microsoft365Service`, `Microsoft365CorrelationPolicy`, snapshot/status/
  capacity/correlation records and five action handlers (`getStatus`, `connect`,
  `disconnect`, `read`, `getContext`). Module README and project file.
- Host: `Microsoft365AuthenticationWindow`, `Microsoft365SettingsHandlers`.

New frontend components:

- `Microsoft365Page`, `Microsoft365DataView`, `Microsoft365ContextPanel`,
  `Microsoft365Fields`, `useMicrosoft365Action`, `Microsoft365SettingsSection`.
- The resource page contains bounded user/group/device/license/member tables,
  inline details, source status, optional report reads and native sign-in controls.

New tests:

- `Wec.Modules.Microsoft365.Tests`: service/cache/license and correlation tests.
- Infrastructure `GraphReaderTests`: real SDK deserialization over fake HTTP.
- Host `Microsoft365BoundaryTests`: composition, timeouts and trusted origins.
- Host `Microsoft365SettingsHandlersTests` and frontend
  `Microsoft365SettingsSection.test.tsx`: non-secret settings persistence,
  validation, preserving existing configuration and visible restart/error states.
- Frontend `Microsoft365.test.tsx`: connection, errors, cancellation, null
  semantics, stale/partial data, paging, licensing and internal navigation.

Changed existing components:

- `Directory.Packages.props`, Infrastructure/Host project references and solution.
- Core `ErrorCode`; generated TypeScript bridge contracts.
- Host `Program`, `MainWindow`, `WebViewBridge`, `BridgeExecutionTimeoutPolicy`
  and `appsettings.json`.
- Host `UserSettingsStore`, frontend `SettingsPage`, its tests and section
  navigation now include non-secret Microsoft 365 connection defaults.
- Frontend route registry and its test, action timeouts, `UserDetailPage` and
  `ClientDetailPage`.
- `AGENTS.md`, `ROADMAP_DECISIONS.md`, `ROADMAP_EXECUTION.md`; new ADR 0021 and
  this implementation report. No EF model, database table or migration changed.

## Known limitations and next steps

1. Live WAM sign-in, consent, Conditional Access, role restrictions and report/
   Intune licensing need a designated test tenant and administrator acceptance.
   The fixture suite cannot certify those deployment conditions.
2. WEC and the existing AD computer projection lack a stable Entra/sourceAnchor
   device identifier and hardware serial. WEC-to-cloud name matches remain
   candidates. Entra-to-Intune joins require unique non-empty device GUIDs.
   Future explicit Inventory evidence collection can strengthen this boundary.
3. AD user SID joins are authoritative evidence only for matching the accounts.
   UPN remains a candidate, conflicting SIDs block a relationship, and no
   identity/ownership is inferred from display names or guessed sourceAnchor.
4. Large tenants exceeding configured bounds require filtered server paging or
   a later measured delta/indexing design. This version never silently lifts
   limits or claims a complete tenant census from a partial collection.
5. Group membership is direct; hidden members, some service-principal members
   in Graph v1.0 and Exchange dynamic distribution groups are not covered.
6. User mail is not mailbox existence. Exchange mailbox size, quota, shared-
   mailbox state and message content are not implemented. Registered device
   owners and Intune associated users are not labelled as primary ownership.
7. Graph supplies SKU part numbers and plan names. Marketing product names
   require a maintained external catalog and have no invented fallback here.
8. Public Microsoft cloud and delegated WAM only. No sovereign-cloud endpoint
   customization, application identity, certificate flow or unattended sync.
9. Connection-form overrides last for the process session. Central Settings
   saves non-secret defaults; applying them requires the existing restart model.
10. The existing two Moderate Vitest audit findings remain; remediation is an
    independent breaking test-tool upgrade. The new dependency override should
    be removed when the selected Graph SDK resolves a fixed Kiota version.

Useful subsequent slices are verified stable device identity capture, Intune
primary-user evidence, large-tenant server filtering/paging, a governed product
catalog, Exchange administrative mailbox metadata, then separately permissioned
Entra Security/Conditional Access read projections. Each must keep the existing
read-only and data-minimization boundaries; no generic integration framework is
needed before a concrete second use case.

## Verification

- Baseline: 739 backend tests; 461 frontend tests. An initial highly parallel
  frontend run timed out in two pre-existing Settings tests; all 461 passed
  when rerun with two workers, without changing those tests or their timeout.
- Integration verification: 815 backend tests, 475 frontend tests, warning-free
  Release build, frontend production build and 418 generated contracts.
- Graph coverage includes mappings, null/missing properties, SDK error parsing,
  401/403/404/429/5xx, continuation limits/cycles/origin validation, retry budgets,
  cancellation, response-size limits, read-only scope and transport guards.
- Module tests cover TTL/retention/eviction, manual refresh, single-flight,
  cancellation/disconnect, stale error preservation, license math, SID/UPN
  conflicts and null/duplicate Entra/Intune identity evidence.
- NuGet transitive vulnerability audit for Infrastructure reports no vulnerable
  packages. NPM High/Critical audit gate passes; two existing Moderate findings
  remain explicitly recorded.
- No real Microsoft 365 tenant or interactive WAM acceptance was exercised.
  Production tenant acceptance and published-host packaging are separate gates.
