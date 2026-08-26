# Wec.Modules.UserManagement

Read-only, AD-authoritative user inventory and User 360 composition (ADR 0019).

## Actions

- `usermanagement/listUsers` — bounded server-side search, filtering, stable
  sorting and paging.
- `usermanagement/getUserProfile` — immutable-ID lookup with identity,
  lifecycle timestamps, direct groups and SID-allowlisted privileged-access
  evidence.

The module references only `Wec.Core`. Active Directory implements the narrow
`IDirectoryUserReadProvider`; this module owns no LDAP code and persists no user
copy. Passwords exist only in the bridge request and in-memory scan credentials.
There are no directory writes or lifecycle workflow mutations.
