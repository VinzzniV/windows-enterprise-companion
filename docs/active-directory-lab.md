# Active Directory lab validation

This runbook validates the read-only LDAP behavior against a disposable or
approved test domain. Do not run it against production solely to create test
data. WEC never writes to Active Directory; any optional fixture creation and
cleanup is performed by the lab administrator outside WEC.

## Required profiles

Use at least these two Windows clients:

1. A workgroup client with no domain override.
2. A domain-joined client, or a workgroup client that can resolve and reach an
   explicitly entered test domain/DC.

For scale validation, use a test OU containing more objects than both the LDAP
page size and the configured UI limit. Recommended fixture: at least 2,000
users and 2,000 computers with `PageSize=500`, `ExampleLimit=20`, and search
limits of 500. Record fixture counts independently before running WEC.

Never place passwords, exported directory data, or real account names in the
repository, screenshots, issue text, or test logs.

## Workgroup preflight

1. Start WEC on the workgroup client without entering a domain or DC.
2. Open **Active Directory** and choose **Test connection** or
   **Analyze directory** once.
3. Verify the screen shows one **No local Active Directory domain** state.
4. Verify **Hygiene unavailable (workgroup)** is disabled and no LDAP request
   is attempted for hygiene.
5. Enter the test domain DNS name. Verify the workgroup state clears and the
   hygiene action becomes available again.

## Bind and error mapping

Run **Test connection** for each approved case and record only the typed status:

| Case | Expected result |
|---|---|
| Current identity with read access | Connected, correct domain and naming context |
| Explicit UPN | Connected; credentials remain session-only |
| Explicit `DOMAIN\\user` | Connected to the same naming context |
| Wrong password | `AUTHENTICATION_FAILED` |
| Unknown domain/DC name | `DNS_RESOLUTION_FAILED` |
| Reachable host with LDAP blocked | `DIRECTORY_UNAVAILABLE` or `CONNECTION_TIMEOUT` |
| Valid bind without subtree read permission | `ACCESS_DENIED` |

Confirm the UI displays the remediation for the typed error and does not expose
an exception, password, or connection string.

## Paging and bounded results

1. Analyze the seeded test domain and compare Users, Disabled users, Groups,
   and Computers against independently recorded exact counts.
2. Run hygiene checks. Verify each rule shows the exact match count but no more
   than `ExampleLimit` examples.
3. Search users and computers. Verify each response contains no more than its
   configured limit and displays the truncated state when the exact total is
   larger.
4. Repeat with `PageSize` smaller than the fixture size. Counts must remain
   unchanged.
5. During the run, observe the WEC process in an approved profiler or Task
   Manager. Increasing the directory from one page to many pages must not grow
   retained result memory in proportion to the full match count.

## Privileged-group and localization cases

Verify a localized domain where Domain Admins has a non-English display name.
The group must still be found by SID. Confirm absent Enterprise/Schema Admins
groups in a child domain are treated as valid absence, direct member counts are
shown, and only the configured number of member examples is displayed.

## Evidence to retain

Record the WEC commit, Windows build, domain functional level, configured
limits, fixture totals, result totals, typed failures, and whether memory stayed
bounded. Store sanitized evidence in the approved test system, not in this
repository. Remove any lab-only accounts/OUs using the lab's normal change and
cleanup procedure.
