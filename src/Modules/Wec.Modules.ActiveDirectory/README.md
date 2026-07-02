# Wec.Modules.ActiveDirectory

Read-only Active Directory analysis (M4). Access strategy: ADR 0006 —
LDAP via the search-only `IDirectoryReader` Core seam, authenticated as the
current Windows identity. The module never writes to the directory; the seam
does not even expose a write operation.

## Bridge actions

| Action | Payload | Result |
|---|---|---|
| `activedirectory/getOverview` | `{}` | `AdOverviewResult` — domain membership, DC list, user/group/computer counts |

## Behavior

- **Workgroup machine:** `domainJoined: false` with empty data — a valid
  answer rendered as "not applicable", never an error.
- **Directory unreachable:** typed `DIRECTORY_UNAVAILABLE` error.
- **Read refused:** typed `ACCESS_DENIED` error (whatever the logged-on user
  may read is what WEC shows; no credential prompts).
- Counts use paged searches with an empty attribute list (RFC 4511 `1.1`),
  so no attribute payload crosses the wire.

## Options (`Wec:ActiveDirectory`)

| Option | Default | Purpose |
|---|---|---|
| `PageSize` | 500 | LDAP paged-search page size |
| `SearchTimeout` | 30 s | Per-request client/server time limit |

## Tests

`tests/Wec.Modules.ActiveDirectory.Tests` — the directory seam and WMI are
mocked; no test touches a real domain.
