# Wec.Modules.UserManagement

Read-only, AD-authoritative user inventory and User 360 composition (ADR 0019).

## Actions

- `usermanagement/listUsers` — bounded server-side search, filtering, stable
  sorting and paging.
- `usermanagement/getProfile` — source-scoped AD or Entra account composition
  (ADR 0022). Reads cached source projections; only `loadDirectoryIdentity`
  requests an explicit bounded AD GUID/SID read. Cloud refresh uses the source
  owner's typed action and expected tenant, then recomposes cached evidence.
- `usermanagement/resolveSid` — explicit, bounded inverse read for a stored
  Windows SID in a selected directory. It returns only a validated scoped GUID;
  the account profile reuses the same session's cached native identity.
- `usermanagement/getUserProfile` — immutable-ID lookup with identity,
  lifecycle timestamps, direct groups and SID-allowlisted privileged-access
  evidence plus SID-matched Inventory device relationships.
- `usermanagement/exportLeaverReview` — writes a bounded Markdown evidence
  checklist assembled from the deliberately selected User 360 profile.

The module references only `Wec.Core`. Active Directory implements the narrow
`IDirectoryUserReadProvider`; this module owns no LDAP code and persists no user
copy. Passwords exist only in the bridge request and in-memory scan credentials.
There are no directory writes or lifecycle workflow mutations.

Leaver review is a read-only, session-local checklist. Checked items are not
persisted and do not represent a workflow status. Export requires an explicit
save-dialog confirmation and contains the evidence currently visible to the
administrator; its Markdown payload is never logged.
Microsoft 365 facts are excluded from this assessment and export. Optional
reports, licenses, groups and device evidence remain independently queryable in
the session-only profile. No person-level merge or immutable-ID/sourceAnchor
assumption is made. A hybrid join requires a valid SID with unique applicable
source coverage; names remain candidates and SID conflicts block the join.

Device links carry Inventory source coverage, observation time, relationship
type and confidence. `Last interactive user` and `Profile present` are evidence,
not ownership; missing SIDs, older snapshots and source failures remain visible.
The bounded device list adds only persisted installed-software, Health, Security
and Nessus summaries. Opening User 360 starts no remote scan or provider sync.
