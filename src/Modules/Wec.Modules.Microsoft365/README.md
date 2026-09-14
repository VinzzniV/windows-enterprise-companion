# Wec.Modules.Microsoft365

`IMicrosoft365GroupContextProvider` exposes cached group object/inventory reads
and direct members with separate query coverage, errors and session revision.
Limited-information objects and duplicate source rows remain intact; this seam
does not start Graph calls or expand nested memberships.

`IMicrosoft365DeviceContextProvider` exposes concrete cached Entra, Intune and
registered-owner facets to Clients. Each query retains its own state and raw
records (including duplicates). Tenant mismatches fail before any read; cache
misses never invoke Graph. Object detail reads remain separate from inventories.
Known Intune IDs use the [Graph v1.0 managedDevice read](https://learn.microsoft.com/en-us/graph/api/intune-devices-manageddevice-get?view=graph-rest-1.0)
with the existing selected fields and `DeviceManagementManagedDevices.Read.All`.
They do not require a tenant-wide inventory or a Primary User query.

`IMicrosoft365UserContextProvider` exposes cached identity, license, direct-group,
registered-device, Intune-userId, sign-in and registration evidence separately.
An explicit SID query uses the documented [`onPremisesSecurityIdentifier eq`
filter](https://learn.microsoft.com/en-us/graph/api/resources/user?view=graph-rest-1.0)
under existing `User.Read.All`, retaining at most two users to detect ambiguity.
It has its own query/coverage state and never promotes a partial general user
inventory to complete coverage. No new fields or permissions are requested.

Read-only Microsoft Graph source. Infrastructure owns Graph/MSAL; this module
owns the bounded session cache, license capacity calculations, correlation
policy and transport-independent handlers. It references only Wec.Core.

Actions: `getStatus`, `connect`, `disconnect`, `read`, `getContext` under the
`microsoft365` bridge module. `read` accepts an enum and optional object GUID,
never an endpoint, query string, scope, token or arbitrary Graph command.

Cache defaults: ten minutes fresh, one hour retention, 32 query entries.
Expired-but-retained data stays visible until explicit refresh. A source-work
gate coalesces concurrent equivalent refreshes, including failures, without
blocking cache-only views or status reads. A separate short lock protects state.
Caller cancellation reaches the provider; a cancelled leader publishes no
partial cache. Disconnect cancels reads and discards all WEC session evidence.

Every query exposes retrieval/attempt/retention times, its own error and coverage,
and session/snapshot revisions. A failed attempt does not extend fact retention
or freshen another query. Failed detail queries count toward the same entry
limit. Session transitions immediately hide the previous context and reject late
results; a cancelled transition requires another explicit connection action.
Future timestamps have unknown freshness. An exact SID observed in a truncated
user collection remains a candidate because uniqueness is unproven.

Graph collections have configurable page/item/response limits. Truncation is
visible; totals are Graph's eventual counts when supplied. Null collections
are errors; an actual empty array is a successful empty result. Read failures
preserve an older snapshot with a typed error. Cached context never invokes Graph.

See ADR 0021 and `docs/microsoft-365-implementation.md` for deployment,
permissions, security boundaries and live acceptance requirements.
