# Wec.Modules.GroupManagement

Source-scoped group profile composition (ADR 0022). This module references only
Core and owns no directory/Graph adapter or persistence. AD and Entra groups
remain separate; display names never join them.

- `groups/getProfile` reads cached identity and membership evidence. Explicit
  `DirectoryIdentity` / `DirectoryMembers` reads use the AD source owner;
  Entra refresh uses Microsoft365's existing typed read action.
- `groups/resolve` resolves one exact AD DN/SID to a scoped GUID. A missing or
  ambiguous identity has no automatic destination.
- `groups/readDirectoryPage` exposes one bounded cached AD source page, with
  an explicit refresh switch, separate query counts and cache/connection state.

Direct membership preserves supported user, device and nested-group IDs.
Unsupported types or absent native IDs remain visible without guessed links.
Nested groups open separately; no recursion, effective permission claim,
directory write, new Graph scope, cloud export or persistent linking is added.

Frontend routes: `/groups`, `/groups/:source/:scope/:objectId`, and
`/groups/resolve?scope=...&dn=...` (or `sid=...`). AD user profiles link direct
group DNs to explicit resolution; source-native member links open the existing
scoped user/device/group profiles. Initial discovery exposes bounded AD pages
and the bounded Entra inventory separately, with source counts and retention.
