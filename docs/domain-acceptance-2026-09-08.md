# Company-environment acceptance — 2026-09-08

Status: **Partially accepted; Kaspersky trust and dedicated lab gates remain open.**

Baseline: merged PR #28 (`e89395f`). Corrections are on
`codex/domain-acceptance`, through `951036a`. The locally built Release host
used the installed runtime profile as a standard Windows user. Remote checks
were restricted to the single client designated in the user conversation;
the user entered the authorized administrator credentials directly in WEC.
Endpoints, account identifiers, certificate fingerprints and raw evidence are
intentionally excluded from this record under D-008.

## Live results

| Area | Result | Evidence and limits |
|---|---|---|
| AD connection and overview | Passed | Current-identity bind, overview and hygiene queries succeeded against the configured directory. The directory contains 755 users and 630 computers; this is not the larger seeded lab fixture. |
| AD user paging | Passed after correction | Default user search previously failed because AD DS rejects multiple server-side sort keys. One selected key now works. Two 25-row pages had no overlapping rows during this run. Concurrent directory changes can still affect independent page requests. |
| AD user search and profile | Passed | Explicit search returned the expected two accounts. User 360 loaded identity, lifecycle evidence and 24 direct groups for the selected profile. No privileged direct membership was reported for that profile. |
| Remote Inventory | Passed | Current identity was denied; explicitly supplied administrator credentials succeeded. A fresh snapshot replaced the stale cache and included 277 installed applications, hardware and approved profile evidence. |
| Remote Health | Passed | All four expected checks completed and persisted: updates, selected services and disk space passed; System Event Log raised a warning for 52 errors in 24 hours. This is a device finding, not a WEC failure. |
| Remote Event Log | Passed | The bounded System-errors preset returned the same 52 events. The UI identifies the query as a live, non-persisted result. |
| Connectivity | Passed with UI limit | Explicit Client 360 connectivity action returned `Ping or WinRM responded`. This combined label alone does not establish that both probes succeeded independently; successful remote CIM reads separately verify the WinRM management path. |
| User/device evidence | Passed | The fresh scan captured 13 unresolved local profiles and no named interactive-user observation. User 360 matched one profile by SID to the designated client and displayed profile-presence evidence, last-use context and medium confidence without claiming ownership. |
| opsi | Passed | The configured provider connected, supplied the designated client's inventory and populated the 159-product dashboard. No package installation or remote write action was performed. |
| Nessus | Passed after correction | A persisted August `Running` state incorrectly blocked recovery after process restart. Reconciliation with the current process gate enabled recovery; the real sync completed with 12 included scans and 634 assets. Existing results remained readable during refresh. |
| Kaspersky | Blocked: certificate trust | The TLS handshake reached the configured endpoint. The configured SHA-256 fingerprint does not match the currently presented, unexpired, self-signed certificate. The UI reports the validation failure explicitly. No certificate bypass or trust-store change was made. |
| Relationship UI | Passed for tested paths | Fixed compressed node names and unbounded list column sizing. The updated User 360 map/list rendered the real device relationship; Tab visibly focused its link and Enter opened the stored Client 360 profile. Broad DPI/screen-reader acceptance remains a separate gate. |
| Manual-only BitLocker | Passed after correction | Cached Inventory originally triggered a hidden live BitLocker query when Client 360 mounted. The updated host remained idle on profile and Inventory open; only `Check BitLocker` queried the target. A denied remote query now reports missing remote rights without recommending local elevation. |
| Leaver review | Passed for session interaction | Six evidence items rendered, the reviewed count changed from zero to one after an explicit checkbox action, and the view remained read-only. No account/group change or workflow case was created. File-export acceptance was not exercised with real personal data. |
| Action Center / Device Cleanup | Smoke passed | Both workspaces loaded real source evidence and retained explicit read-only boundaries. No cleanup decision or probe against another device was performed. |

## Corrections and automated gates

- Single-key LDAP sort follows AD DS's documented restriction:
  [Microsoft AD DS sort control specification](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-adts/6b7b93f1-7c1a-45c2-9544-c067b94bba20).
- LDAP and CIM diagnostic logging no longer includes raw filters/WQL or
  expected provider exception objects that can contain user evidence.
- A post-restart log sample covering profile navigation contained 40 records,
  zero raw SID values, zero `LdapFilter`/`WqlQuery` fields, zero Error/Fatal
  entries and zero BitLocker actions before the explicit BitLocker test.
  This is a bounded runtime sample, not proof about all possible log paths.
- Browserslist was updated from 4.28.4 to 4.28.9, resolving the newly reported
  high-severity dependency findings.
- Release build: zero warnings/errors. Backend: **743 passed**. Frontend:
  **460 passed** across 77 files. Generated bridge types: **388 current**.
  Frontend production build and `git diff --check` passed. NPM audit: **zero
  vulnerabilities**.
- Added regressions cover AD's single-key wire control, cached-profile
  BitLocker behavior and interrupted Nessus phases versus an active sync.

## Remaining acceptance gates

1. Independently verify the current KSC certificate fingerprint or establish
   the correct Windows trust chain, then rerun the read-only Kaspersky test.
   A fingerprint observed through the failing TLS connection alone is not an
   independent identity check.
2. Execute the dedicated cases in `active-directory-lab.md`: large seeded
   directory, controlled invalid credentials, denied subtrees, blocked LDAP,
   child-domain/localization cases and measured paging memory. These require
   a suitable lab; production accounts and firewall settings were not altered
   to manufacture failure cases.
3. Complete broader accessibility/DPI review and approved export-file
   acceptance. Automated accessibility, source-state and export regressions
   passed; they do not substitute for every manual acceptance case.
4. Before merge/release, run PR CI and release-free packaging for these code
   changes. No push, merge, version tag, installer publication or GitHub
   Release was performed in this acceptance run.
