# Wec.Modules.ActiveDirectory

Read-only Active Directory analysis (M4). Access strategy: ADR 0006 —
LDAP via the search-only `IDirectoryReader` Core seam, authenticated as the
current Windows identity. The module never writes to the directory; the seam
does not even expose a write operation.

## Bridge actions

| Action | Payload | Result |
|---|---|---|
| `activedirectory/getOverview` | `{}` | `AdOverviewResult` — domain membership, DC list, user/group/computer counts |
| `activedirectory/getHygiene` | `{}` | `AdHygieneResult` — privileged groups + hygiene rules (inactive users/computers, password-never-expires, disabled-but-privileged) |

## Behavior

- **Workgroup machine:** `domainJoined: false` with empty data — a valid
  answer rendered as "not applicable", never an error.
- **Directory unreachable:** typed `DIRECTORY_UNAVAILABLE` error.
- **Read refused:** typed `ACCESS_DENIED` error (whatever the logged-on user
  may read is what WEC shows; no credential prompts).
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
