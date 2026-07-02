# ADR 0006: Active Directory Access Strategy

- **Status:** Accepted (2026-07-02)
- **Date:** 2026-07-02
- **Deciders:** Vinz
- **Supersedes:** —

## Context

M4 adds read-only Active Directory analysis (domain detection, DC discovery,
user/group/privileged-group overviews, hygiene findings such as inactive
accounts and password-never-expires). Constraints from the roadmap and the
existing architecture:

- **Strictly read-only.** Nothing in M4 may write to the directory.
- Must degrade gracefully when the machine is not domain-joined and when the
  current user lacks read rights; must not require domain admin.
- Modules depend only on `Wec.Core` — the directory access technology must
  hide behind a Core seam, like `IWmiQueryService` does for CIM.
- Tests mock the seam; no test may talk to a real directory.

### Options considered

| Option | Assessment |
|---|---|
| **`System.DirectoryServices.Protocols` (S.DS.P)** — direct LDAP | Thin, fully supported on .NET 10, no COM. Explicit control over search scope, paging, timeouts and referrals. More code (attribute parsing, paging loop), but that code lives in exactly one Infrastructure class. |
| `System.DirectoryServices` (ADSI/COM) | Higher-level (`DirectorySearcher`), but COM-based with subtle lifetime/threading issues inside a WinForms host, and its API surface mixes read and write on the same objects — a read-only guarantee by convention only. |
| `System.DirectoryServices.AccountManagement` | Most convenient for principals, but effectively legacy, slow (one query per principal), limited filtering, and also read/write on the same types. |
| PowerShell `ActiveDirectory` module | Requires RSAT on every machine and drags a PowerShell runspace into the process for data we can get over plain LDAP. Wrong dependency weight. |

## Decision

1. **LDAP via `System.DirectoryServices.Protocols`**, implemented in a single
   Infrastructure service behind a new Core seam.
2. **Core seam `IDirectoryReader`** following the `IWmiQueryService` pattern:
   one search method (base DN, LDAP filter, attribute allowlist, scope) that
   returns `Result<IReadOnlyList<DirectoryEntryData>>`, where
   `DirectoryEntryData` is an attribute dictionary. The interface exposes
   **search only** — the read-only rule is enforced by the seam's shape, not
   by convention. Domain interpretation (what makes an account "inactive")
   lives in the module, so it is unit-testable against the mocked seam.
3. **Authentication: current Windows identity only** (Negotiate). No
   credential prompt, no credential storage, no alternate-credentials feature
   in M4. Whatever the logged-on user may read is what WEC shows — consistent
   with ADR 0002 (capabilities of the invoking identity, never self-elevate).
4. **Domain context detection stays local:** domain membership comes from
   `Win32_ComputerSystem` via the existing `IWmiQueryService`; the LDAP
   server is addressed by domain name (locator-based), never hardcoded.
   Not domain-joined ⇒ the module reports `NotApplicable`-style results,
   no LDAP connection is attempted.
5. **Expected failures are `Result` errors, not exceptions:** new
   `ErrorCode.DirectoryUnavailable` for unreachable/failed LDAP;
   `AccessDenied` (existing) carries through when the directory refuses a
   read. Both surface visibly in the UI, never as empty lists.
6. **Query discipline:** paged searches (`PageResultRequestControl`),
   explicit attribute allowlists per check (never `*`), client- and
   server-side time limits from options (`Wec:ActiveDirectory:*` — page
   size, timeouts, inactivity threshold days). No tunables in code.

## Consequences

- Attribute parsing (e.g. `lastLogonTimestamp` file-time handling,
  `userAccountControl` bit flags) is our code — unit-tested in the module
  against fixture dictionaries, which is exactly where such logic belongs.
- A future "connect to another forest with explicit credentials" feature
  would extend the seam and needs an ADR revision (credential handling
  policy).
- Large directories are handled by paging; M4 checks aggregate counts and
  bounded lists (top-N) instead of materializing entire user tables in the
  UI.
- `Wec.Modules.ActiveDirectory` + `frontend/src/features/activedirectory`
  mirror the established module layout; no schema/persistence in slice 1
  (live analysis first — persistence only if reporting needs it later,
  same YAGNI call as M3).
