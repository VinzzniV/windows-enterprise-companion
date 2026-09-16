# Consolidation — live acceptance, 2026-09-16

Status: `COMPLETED_WITH_FINDINGS`; F-01 prevents a clean acceptance.
Build under test: `17f53ef`, Release, installed runtime profile.
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
| Existing management sources | Partial acceptance | AD, Kaspersky and opsi returned target evidence. Nessus returned historical findings with Partial coverage. Sources stayed separate and name/address matches remained candidates. Full Nessus coverage is not established. |
| Microsoft 365 connection | Pass | Existing configuration and WAM session connected without credential entry into automation. The first Graph read succeeded. No scopes or consent settings were changed. |
| Entra working-set users | Pass | The bounded read loaded 760 users. Searching the current account returned two distinct source accounts rather than merging by name. |
| Entra account to AD identity | Pass | An explicit exact-SID lookup in the existing directory returned the unique AD account. The profile displayed the concrete synchronized-SID relationship and AD authority. |
| AD account to group identity | Pass | A direct group link opened the exact-DN resolver; resolving it opened the scoped group GUID profile with fresh source evidence. |
| AD group direct members | Fail | The DC rejected the LDAP request control. Group identity remained available, member evidence stayed unavailable and the source error was visible. See F-01. |
| Entra direct account groups | Pass | Explicit source read completed and exposed individually navigable direct groups. No transitive/effective-permission claim was made. |
| Entra group identity and direct members | Pass | A source-native group link opened its scoped profile. The individual group read completed at 12:00:14 and the direct-member read at 12:00:24, returning ten typed account links. Identity and member freshness remained independent; nested membership was not expanded. |
| Account licenses and tenant catalogue | Pass | Both reads returned fresh evidence independently. The account-to-SKU link opened the matching catalogue item; its reverse link displayed three matching loaded accounts with tenant/SKU filters. |
| Intune/device relationship journeys | Pass | Explicit reads loaded 205 Intune records and 1,141 Entra device records, with separate query coverage and source-declared counts. The designated device had an exact azureADDeviceId/deviceId relationship. Its associated-user link opened the Entra account; the account Devices section linked back using the exact userId. The label explicitly disclaimed primary-user/ownership semantics. Windows/AD name matches stayed candidates. The cloud Inventory section exposed no Windows scan without selecting an exact Windows target. |
| Explicit Ping/WinRM connectivity control | Pass | The explicit single-target check returned "Ping or WinRM responded" and Connected. This observes the combined control result, not separate successful results for both protocols. |
| Narrow-window desktop acceptance | Pass | At an observed 733 x 654 outer-window size, filters and profile tabs wrapped, the scrollable navigation drawer remained usable, Escape closed it and restored focus, and Ctrl+K search opened the correct source-scoped device. The earlier resize/stop limitation is superseded for these journeys. |
| Disconnect/cache clearing | Pass | Disconnect changed the connection to Not connected and disabled its disconnect control. The Intune-filtered working set then contained zero matches; non-cloud source records remained loaded. The app was left open, with Microsoft 365 disconnected and the Windows admin session unchanged. |
| Real cross-tenant switch | Not performed | No second authorized configured tenant was available. Same-tenant sign-out/cache clearing passed; automated generation/cancellation coverage is not claimed as a live cross-tenant test. |

## F-01 — unsupported multi-attribute AD server sorting

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

Required correction: send an AD-supported request and preserve deterministic
paging, duplicate names and limited-information members. Simply suppressing
the control error or dropping ordering guarantees is insufficient. Add a
regression at the LDAP request/ordering boundary and repeat the live member,
group-page and affected user-sort checks before marking F-01 resolved.

## Closure requirements

Correct F-01 and repeat its affected checks before closing live acceptance.
The executed checks are complete; no production correction was made in this
acceptance-only follow-up. Full Nessus coverage and real cross-tenant validation
remain unverified and must remain explicit in any release decision.
Automated regression/packaging from the implementation milestone remains
valid for that build; it did not establish compatibility of this live AD
request. No release readiness is claimed by this report.
