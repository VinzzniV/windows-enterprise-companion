# Consolidation — live acceptance, 2026-09-16

Status: `FIXES_VERIFIED_WITH_ACCEPTED_LIMITS`; F-01 and F-02 are resolved.
Initial build under test: `17f53ef`, Release, installed runtime profile.
The correction/retest section below supersedes the initial failed observations.
Scope: the single workstation designated by the user in this task (D-003),
existing configured management sources and the existing Microsoft 365 tenant.
The user entered the admin credential directly in WEC. Hostnames, account
names, tenant identifiers, object IDs and raw source records are not copied
into this report. No remote write, permission change or release was performed.

## Observed results

| Check | Result | Evidence and limits |
| --- | --- | --- |
| Exact Windows target and Inventory | Pass | The stored fully qualified target opened correctly. Explicit refresh completed at 11:27:12 local time with 215 software entries. The previous snapshot remained visible while refreshing. |
| Remote Health | Pass | Explicit run completed at 11:28:23: all four expected checks observed, four Healthy, no Critical/Warning/Unknown result. This is the configured Health scope, not a complete security assessment. |
| Detailed Event Log | Pass | The System errors/last-24-hours preset returned 50 matching events. Source Windows events are findings, not WEC query failures. No raw event contents were exported. |
| Stored summary refresh | Pass | The overview adopted the new Inventory and Health timestamps after explicit summary refresh. Fourteen unresolved local profiles remained unknown; no named owner was invented. |
| Existing management sources | Pass within accepted limits | AD, Kaspersky and opsi returned target evidence. Initial Nessus Partial coverage led to F-02; current inventory refresh passed after correction. Full environment coverage is neither established nor required by the user's clarification. Sources and name/address candidates remain separate. |
| Microsoft 365 connection | Pass | Existing configuration and WAM session connected without credential entry into automation. The first Graph read succeeded. No scopes or consent settings were changed. |
| Entra working-set users | Pass | The bounded read loaded 760 users. Searching the current account returned two distinct source accounts rather than merging by name. |
| Entra account to AD identity | Pass | An explicit exact-SID lookup in the existing directory returned the unique AD account. The profile displayed the concrete synchronized-SID relationship and AD authority. |
| AD account to group identity | Pass | A direct group link opened the exact-DN resolver; resolving it opened the scoped group GUID profile with fresh source evidence. |
| AD group direct members | Pass after correction | Initially the DC rejected the LDAP request control. After F-01's correction the same group returned five direct members. See the retest evidence below. |
| Entra direct account groups | Pass | Explicit source read completed and exposed individually navigable direct groups. No transitive/effective-permission claim was made. |
| Entra group identity and direct members | Pass | A source-native group link opened its scoped profile. The individual group read completed at 12:00:14 and the direct-member read at 12:00:24, returning ten typed account links. Identity and member freshness remained independent; nested membership was not expanded. |
| Account licenses and tenant catalogue | Pass | Both reads returned fresh evidence independently. The account-to-SKU link opened the matching catalogue item; its reverse link displayed three matching loaded accounts with tenant/SKU filters. |
| Intune/device relationship journeys | Pass | Explicit reads loaded 205 Intune records and 1,141 Entra device records, with separate query coverage and source-declared counts. The designated device had an exact azureADDeviceId/deviceId relationship. Its associated-user link opened the Entra account; the account Devices section linked back using the exact userId. The label explicitly disclaimed primary-user/ownership semantics. Windows/AD name matches stayed candidates. The cloud Inventory section exposed no Windows scan without selecting an exact Windows target. |
| Explicit Ping/WinRM connectivity control | Pass | The explicit single-target check returned "Ping or WinRM responded" and Connected. This observes the combined control result, not separate successful results for both protocols. |
| Narrow-window desktop acceptance | Pass | At an observed 733 x 654 outer-window size, filters and profile tabs wrapped, the scrollable navigation drawer remained usable, Escape closed it and restored focus, and Ctrl+K search opened the correct source-scoped device. The earlier resize/stop limitation is superseded for these journeys. |
| Disconnect/cache clearing | Pass | Disconnect changed the connection to Not connected and disabled its disconnect control. The Intune-filtered working set then contained zero matches; non-cloud source records remained loaded. The app was left open, with Microsoft 365 disconnected and the Windows admin session unchanged. |
| Real cross-tenant switch | Not performed | No second authorized configured tenant was available. Same-tenant sign-out/cache clearing passed; automated generation/cancellation coverage is not claimed as a live cross-tenant test. |

## F-01 — unsupported multi-attribute AD server sorting (resolved)

The direct member read failed at 11:39:52 with
`DirectoryOperationException`: the server rejected a critical request control
(`The server does not support the control. The control is critical.` and
`Error processing control`).
The identity read against the same DC succeeded immediately beforehand.

`DirectoryGroupReadService` requests `name` plus `sAMAccountName` as a tie
breaker. `LdapDirectoryReader` serializes both as server sort keys.
[Microsoft's AD sorting documentation](https://learn.microsoft.com/en-us/windows/win32/adsi/sorting-the-search-results-with-idirectorysearch)
states that AD supports only one sort attribute and cannot sort on
`distinguishedName`. This matches the observed control rejection.

Code inspection shows the same request construction in general group paging
and in user paging when a secondary sort key is selected. Those additional
paths are affected by the same mechanism but have not each been reproduced
live. The current mocked module tests do not exercise DC control support.

Required correction, now implemented: send an AD-supported request and preserve deterministic
paging, duplicate names and limited-information members. Simply suppressing
the control error or dropping ordering guarantees is insufficient. Add a
regression at the LDAP request/ordering boundary and repeat the live member,
group-page and affected user-sort checks before marking F-01 resolved.

### Correction and live retest

Commit `585bfeb` implements ADR 0023: one LDAP server key plus a bounded
ordered prefix for deterministic secondary sorting. The configured default
maximum is 10,000 retained entries, independent of the complete query count.
Duplicate/missing names use the full DN as the final tie breaker. Numeric
last-logon values sort numerically. No new fields, directory writes or
persistent directory snapshot were added.

An explicit temporary acceptance harness used the actual compiled AD module
providers and LDAP reader with the current Windows identity, pinned to the
already approved DC. Only aggregate outcomes were emitted; WMI was disabled.
This was a live follow-up, not an automatic directory-dependent unit test.

- The same group identity resolved uniquely and returned all five direct members.
- General group pages returned distinct windows from 2,940 source results.
- A narrower 30-result group search also returned disjoint pages with LDAP
  PageSize=2, deliberately crossing transport-page boundaries.
- DisplayName, SamAccountName, Department, CreatedAt and LastLogon each passed
  ascending and descending reads. A two-account filtered set produced disjoint
  one-row pages and identical repeated first pages in all ten combinations.
- A 25,000-row fixture keeps only the requested 50-entry prefix, counts all
  entries and returns the exact ten-row offset window. Other regressions cover
  duplicate/missing/Unicode values, single-key wire encoding, numeric values,
  cancellation and rejecting excessive pages before a directory query.

## F-02 — persisted Nessus running flag survived process exit (resolved)

Correction commit: `951b3de`.

The initial stored state claimed `ImportingHistory`, running since 2026-09-11,
with the last successful sync from 2026-09-10. No corresponding WEC process
was running when the correction build was started. The saved running flag
prevented stale-cache refresh. The stored inventory contained 12 completed
scans, no recorded scan errors, 640 assets and 47,726 findings.

The status read now distinguishes the current process sync gate from a saved
running flag. An interrupted run becomes stopped with a retry explanation.
Stored-only reads do not mutate the database or start synchronization.
Published assets remain Partial rather than Unavailable when a later stage
fails; specific errors are not hidden behind a generic stale message. The
dashboard likewise keeps retained assets visible. Current-inventory freshness
is recorded immediately after successful publication, before optional history
backfill; cancellation clears the running state without deleting inventory.

The corrected installed-profile WEC displayed the interruption and its existing
Vulnerabilities auto-refresh path started a new real sync at 12:27:38 local.
All 12 current scans imported with zero recorded scan errors. Current inventory
publication succeeded at 12:28:36: **669 assets and 50,219 findings**. The UI
showed that new successful timestamp while history backfill continued.

The UI classified 274 assets as matched and 395 as unmatched against its loaded
host-name set. That is a correlation result, not an environment-coverage
percentage or proof of physical identity. The source rows lack host UUIDs and
323 lack FQDNs; report locators and candidates must remain distinct. Do not turn
unmatched records into missing scans or force name-based object merges.

Per the user's explicit clarification, complete Nessus environment coverage
and completion of optional historical backfill are not required for this fix's
acceptance. The last observed backfill state was running, not a claimed full
historical success.

## Correction verification

- Release build: zero warnings/errors; 1,043 backend tests pass.
- 559 frontend tests in 97 files pass; dashboard retained-result regression
  and frontend production build pass.
- 494 generated contracts remain current; dependency checks pass for all
  20 production projects; NPM High gate passes with the two existing Moderate
  Vitest development advisories unchanged.
- Real AD retests and real Nessus current-inventory publication passed as above.

## Closure requirements

The requested defects and Nessus investigation are complete. Real cross-tenant
validation remains unperformed; no second tenant was supplied. Previously
recorded acceptance results retain their original scope. Packaging evidence
from `17f53ef` applies to that build, not automatically to the correction.
No tag, installer publication or GitHub Release is authorized by this report.
