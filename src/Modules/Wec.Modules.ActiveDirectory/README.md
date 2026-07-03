# Wec.Modules.ActiveDirectory

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

`DirectoryConnectionRequest` = `{ domain?, server?, userName?, userDomain?,
password? }`. Empty analyzes this machine's own domain as the current user.
An explicit `domain` skips the local WMI detection (a workgroup machine can
analyze a foreign domain); `server` pins the connection to one DC.

## Behavior

- **Workgroup machine without a domain override:** `domainJoined: false`
  with empty data — a valid answer rendered as "not applicable", never an
  error.
- **Failures are diagnostically distinct** (ADR 0006 revision):
  `DNS_RESOLUTION_FAILED` (own probe, with a point-DNS-at-the-domain hint),
  `AUTHENTICATION_FAILED` (LDAP bind rejected), `DIRECTORY_UNAVAILABLE`
  with a DC-down/firewall explanation, `CONNECTION_TIMEOUT`, `NOT_FOUND`
  (naming context missing) and `ACCESS_DENIED` (read refused).
- Counts use paged searches with an empty attribute list (RFC 4511 `1.1`),
  so no attribute payload crosses the wire.
- Hygiene rules report **exact counts** with **bounded example lists**
  (`ExampleLimit`); privileged groups are resolved by well-known SID
  (Domain/Enterprise/Schema Admins RIDs 512/519/518, Builtin Administrators
  S-1-5-32-544), so localized group names do not matter.
- Known limitations (documented, deliberate for M4): "inactive" relies on
  `lastLogonTimestamp` (replicated with up to ~14 days slack; accounts that
  never logged on are not matched); privileged group membership counts
  **direct** members only — nested membership and ranged retrieval of
  groups with >1500 direct members come later if needed.

## Options (`Wec:ActiveDirectory`)

| Option | Default | Purpose |
|---|---|---|
| `PageSize` | 500 | LDAP paged-search page size |
| `SearchTimeout` | 30 s | Per-request client/server time limit |
| `InactivityThreshold` | 90 days | lastLogonTimestamp age that counts as inactive |
| `ExampleLimit` | 20 | Maximum example accounts/members per rule or group |

## Tests

`tests/Wec.Modules.ActiveDirectory.Tests` — the directory seam and WMI are
mocked; no test touches a real domain.
