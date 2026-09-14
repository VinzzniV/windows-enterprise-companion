# Wec.Modules.ActiveDirectory

`activedirectory/readComputerList` explicitly discovers up to 100 computers in
a verified directory DNS scope. The default call is cache-only; `refresh` uses
the existing allowlisted LDAP search. Narrower queries reach beyond a truncated
result. GUID/SID profile reads reuse these observations in the same credential
context and bounded cache, preserving conflicts, query coverage and original age.

Computer inventory preserves directory scope, optional objectGUID/objectSid and
nullable account state (ADR 0022). Missing/malformed IDs are unknown, never name
replacements; same-name records stay distinct. Reads use the existing bounded
search-only LDAP seam with an explicit attribute allowlist.

`IDirectoryGroupReadProvider` adds exact GUID/SID/DN group reads, paged group
search and direct-member pages (ADR 0022). Every read verifies the actual
RootDSE naming context. Identity reads retain at most two records; pages retain
at most 100. Existing LDAP time limits apply; source counts cover the matches
visible to this connection, not other naming contexts. Group types and IDs may
be unknown. Member rows preserve non-user and limited-information objects.
Direct membership uses the `memberOf` backlink, with no recursion or effective
permission calculation; primary groups and external-directory completeness
are not covered. The four privileged-group allowlist remains independent.
Group identity, member and list queries have independent in-memory read state.
Their shared connection context invalidates all entries on a context change.
Failures retain earlier facts only until the original retention deadline;
failure-only reads count toward `IdentityCacheMaximumEntries`. No cache read
performs LDAP I/O, and late results from a former context are discarded.

`IDirectoryUserListProvider` supplies a minimal source-owned user list for the
working set: scoped GUID/SID, names, nullable account state and department.
It reuses the bounded AD page reader (maximum 100 rows per page) without
composing profiles or making per-account membership queries. RootDSE scope is
checked before searching. Pages retain source totals, duplicates and their own
read/error/expiry metadata, bounded by `IdentityCacheMaximumEntries`. Cache
reads do not wait for LDAP; context changes discard old pages and late results.
`usermanagement/readDirectoryPage` exposes this projection with cache-only
behavior by default; `refresh: true` explicitly reads the selected page.

Read-only Active Directory analysis (M4). Access strategy: ADR 0006 (revised
2026-07-03) — LDAP via the search-only `IDirectoryReader` Core seam,
authenticated as the current Windows identity or with optional explicit
credentials (ADR 0007 policy: in-memory only, never persisted). The module
never writes to the directory; the seam does not even expose a write
operation.

## Bridge actions

| Action | Payload | Result |
|---|---|---|
| `activedirectory/getOverview` | `{ connection?: DirectoryConnectionRequest }` | `AdOverviewResult` — domain membership, DC list, user/group/computer counts |
| `activedirectory/getHygiene` | `{ connection?: DirectoryConnectionRequest }` | `AdHygieneResult` — privileged groups + hygiene rules (inactive users/computers, password-never-expires, disabled-but-privileged) |
| `activedirectory/getHygieneRulePage` | `{ ruleId, evaluatedAtUtc, query?, page?, pageSize?, connection? }` | `AdHygieneRulePage` — one stable, exact-count page for an allowlisted hygiene rule; `query` is a literal substring over CN/account, page size is capped at 100 |
| `activedirectory/getPrivilegedGroupMemberPage` | `{ groupDistinguishedName, query?, page?, pageSize?, connection? }` | `AdPrivilegedGroupMemberPage` — one stable, exact-count page of direct members for a currently SID-validated privileged group, including object type, account status and last activity; page size is capped at 100 |
| `activedirectory/testConnection` | `{ connection?: DirectoryConnectionRequest }` | `TestDirectoryConnectionResult` — the RootDSE bind every analysis starts with; a passing test means overview/hygiene can connect |
| `activedirectory/searchComputers` | `{ nameFilter?, includeDisabled?, connection? }` | `AdComputerSearchResult` — the Get-ADComputer-with-filter equivalent; feeds the Clients workspace. Substring match by default, user-typed `*` wildcards pass through; enabled computers only unless `includeDisabled` |
| `activedirectory/searchUsers` | `{ baseDistinguishedName?, includeDisabled?, connection? }` | `AdUserSearchResult` — user listing scoped to an OU (base DN) or the whole domain, including group memberships as plain CNs. Used by the Employee Lifecycle feature to show which accounts exist in a department OU. Truncates at `UserSearchLimit` |

`DirectoryConnectionRequest` = `{ domain?, server?, userName?, userDomain?,
password? }`. Empty analyzes this machine's own domain as the current user.
An explicit `domain` skips the local WMI detection (a workgroup machine can
analyze a foreign domain); `server` pins the connection to one DC.

Credential normalization: `userName` accepts `user@domain.tld` (UPN — the
credential domain stays empty), `DOMAIN\user` (embedded domain wins over
`userDomain`), or a plain user with `userDomain`. A plain user without any
credential domain falls back to `domain`; with neither set the request is
rejected as `INVALID_REQUEST` naming the accepted forms.

## Behavior

- **Workgroup machine without a domain override:** `domainJoined: false`
  with empty data — a valid answer rendered as "not applicable", never an
  error.
- **Failures are diagnostically distinct** (ADR 0006 revision):
  `DNS_RESOLUTION_FAILED` (own probe, with a point-DNS-at-the-domain hint),
  `AUTHENTICATION_FAILED` (LDAP bind rejected), `DIRECTORY_UNAVAILABLE`
  with a DC-down/firewall explanation, `CONNECTION_TIMEOUT`, `NOT_FOUND`
  (naming context missing) and `ACCESS_DENIED` (read refused).
- Counts use paged searches with an empty attribute list (RFC 4511 `1.1`).
  The provider counts all server pages but retains no entries for count-only
  queries. Hygiene and interactive searches retain only their configured
  example/result limit, so a large directory is never fully materialized in
  the client.
- An explicit full-list request for one hygiene rule reuses the original
  evaluation timestamp, applies an RFC-4515-escaped CN/account filter, asks AD
  for a stable `sAMAccountName` sort, counts every filtered match and retains
  only the requested 50-row UI page. Unknown Rule IDs, arbitrary LDAP filters
  and page sizes above 100 are rejected.
- Hygiene rules report **exact counts** with **bounded example lists**
  (`ExampleLimit`); privileged groups are resolved by well-known SID
  (Domain/Enterprise/Schema Admins RIDs 512/519/518, Builtin Administrators
  S-1-5-32-544), so localized group names do not matter. Their exact direct
  counts and bounded examples use the `memberOf` backlink instead of the
  ranged multi-value `member` attribute.
- The Active Directory page renders those bounded examples as sortable
  identity tables. It parses escaped distinguished names into a CN and a
  readable domain/container path, while the row detail preserves the complete
  DN and offers an explicit copy action. The page-level preview search filters
  only the loaded examples and says so next to both the exact total and loaded
  count. Each rule with additional results also offers an on-demand,
  server-filtered and paginated name/account browser. Every non-empty
  privileged group has a separate on-demand direct-member browser using the
  same SID-validated group identity; it adds actual object type, account state,
  last activity and server-side CN/account search. The frontend presents a
  confirmed enabled account as Lifecycle `Current`, a disabled account as
  Lifecycle `Disabled`, a missing `userAccountControl` as Availability
  `Unknown`, and non-account objects as Availability `Not applicable`.
  Bridge values remain available only in technical descriptions and identity
  details; LDAP derivation and paging are unchanged.
- Known limitations (documented, deliberate for M4): "inactive" relies on
  `lastLogonTimestamp` (replicated with up to ~14 days slack; accounts that
  never logged on are not matched); privileged group membership counts
  **direct `memberOf` backlinks** only — nested/transitive membership and the
  separate `primaryGroupID` relationship are not expanded.
- `IDirectoryUserReadProvider` is the read-only cross-module seam for User
  Management (ADR 0019). It pages and counts on the server with allowlisted
  search, account-state, department and OU filters; uses AD `objectGUID` as the
  stable identity; and exposes only the approved identity, lifecycle and direct
  group fields. The legacy bounded `searchUsers` bridge action is unchanged.

## Options (`Wec:ActiveDirectory`)

| Option | Default | Purpose |
|---|---|---|
| `PageSize` | 500 | LDAP paged-search page size |
| `SearchTimeout` | 30 s | Per-request client/server time limit |
| `InactivityThreshold` | 90 days | lastLogonTimestamp age that counts as inactive |
| `ExampleLimit` | 20 | Maximum example accounts/members per rule or group |
| `ComputerSearchLimit` | 500 | Upper bound for the computer search behind the Clients workspace (result carries a `truncated` flag) |
| `UserSearchLimit` | 500 | Upper bound for the OU/domain user search (result carries a `truncated` flag) |

## Tests

`tests/Wec.Modules.ActiveDirectory.Tests` — the directory seam and WMI are
mocked; no automated test touches a real domain. The infrastructure suite
contains a 25,000-entry paged fixture that verifies exact counts, offsets and
bounded materialization. Use the optional [Active Directory lab runbook](../../../docs/active-directory-lab.md)
for workgroup, domain, credentials, paging, and large-directory smoke checks.
# Scoped computer identity reads

User GUID and SID reads also validate the returned identity and, when supplied,
the expected directory scope. The SID path resolves a user GUID through a
bounded exact account-SID filter; duplicate matches are an explicit failure.
It does not fall back to display names or UPN and preserves the existing
SID-validated privileged-group allowlist.

ADR 0022 adds `IDirectoryComputerReadProvider` for explicit GUID/SID reads.
It verifies the requested directory against RootDSE's default naming context,
uses an allowlisted bounded computer query, and preserves duplicate results.
Returned identity evidence must match the requested ID. Names never select an
identity and these reads expose no directory write capability.

Computer identity results use a bounded process cache (64 entries, ten minutes
fresh, one hour retained by default). Credentials only contribute to an opaque
in-memory context fingerprint; they are not retained in snapshots. Changing the
connection/context discards cached evidence and rejects late results. Explicit
refresh failures keep prior facts until their original expiry. Cache-only reads
never invoke LDAP and remain available during source reads.
