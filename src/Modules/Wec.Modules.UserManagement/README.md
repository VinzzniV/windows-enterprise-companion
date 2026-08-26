# Wec.Modules.UserManagement

Read-only, AD-authoritative user inventory and User 360 composition (ADR 0019).

## Actions

- `usermanagement/listUsers` — bounded server-side search, filtering, stable
  sorting and paging.
- `usermanagement/getUserProfile` — immutable-ID lookup with identity,
  lifecycle timestamps, direct groups and SID-allowlisted privileged-access
  evidence plus SID-matched Inventory device relationships.

The module references only `Wec.Core`. Active Directory implements the narrow
`IDirectoryUserReadProvider`; this module owns no LDAP code and persists no user
copy. Passwords exist only in the bridge request and in-memory scan credentials.
There are no directory writes or lifecycle workflow mutations.

Device links carry Inventory source coverage, observation time, relationship
type and confidence. `Last interactive user` and `Profile present` are evidence,
not ownership; missing SIDs, older snapshots and source failures remain visible.
