# IT Lifecycle / Environment Health MVP

The existing `Wec.Modules.EmployeeLifecycle` project and bridge module name are
retained to keep host composition, navigation and existing installations
compatible. The registered feature is now a read-only device hygiene view that
correlates Active Directory with Kaspersky Security Center (KSC).

## Data flow

```text
Active Directory -- IAdComputerInventoryProvider --\
                                                ItHygieneService -> UI
KSC OpenAPI ------ KasperskySecurityCenterClient /
```

- AD reuses the existing LDAP reader, domain discovery, credentials and paging.
  `lastLogonTimestamp` is exposed as `LastLogonDate`; like every replicated AD
  last-logon value it can lag behind the exact logon time.
- KSC uses its HTTPS OpenAPI (default TCP 13299), authenticates with KSCBasic,
  calls `HostGroup.FindHosts`, reads result chunks and releases the temporary
  accessor. No host, group, product or task mutation is called.
- Computer names are trimmed, upper-cased and reduced from FQDN to short name.
  This is intentionally the only identity correlation in phase 1.

## Assessment

The service emits only these findings:

| Finding | Condition |
|---|---|
| `MissingKaspersky` | enabled AD computer, no normalized KSC match |
| `OrphanKaspersky` | KSC computer, no normalized AD match |
| `StaleAd` | AD LastLogonDate exceeds warning/critical threshold |
| `StaleKaspersky` | KSC LastSeen exceeds warning/critical threshold |
| `OutdatedAgent` | numeric Agent version is below configured target |
| `OutdatedKes` | numeric KES version is below configured target |

Unknown timestamps and unconfigured/unknown target versions do not create a
finding. A critical stale finding results in `CleanupCandidate`; this is a
display status only and never triggers cleanup.

## Bridge action

The existing bridge module name remains `employeelifecycle`.

| Action | Payload | Result |
|---|---|---|
| `getHygiene` | optional AD and KSC connection/credential overrides | correlated `ItHygieneResult` |

Credentials are request-scoped and held in memory only. KSC has a separate
session sign-in under Settings so it does not replace the global Windows/AD
administrator. The frontend uses the newest saved domain controller. KSC server,
thresholds, target versions and an optional private-certificate thumbprint are
edited under **Settings → IT Lifecycle / Environment Health**. Saving merges
the values into `%APPDATA%\Wec\usersettings.json`; they apply after restart.
Passwords are never part of the saved settings.

## Options (`Wec:ItLifecycle`)

| Option | Default | Purpose |
|---|---:|---|
| `InventoryLimit` | 10000 | maximum devices read from each source |
| `StaleWarningDays` | 60 | warning threshold |
| `StaleCriticalDays` | 90 | cleanup-candidate threshold |
| `TargetAgentVersion` | empty | desired Network Agent version; empty disables the rule |
| `TargetKesVersion` | empty | desired KES version; empty disables the rule |
| `Kaspersky:Server` | empty | KSC Administration Server host name |
| `Kaspersky:Port` | 13299 | KSC OpenAPI TLS port |
| `Kaspersky:RequestTimeout` | 60 seconds | per-request timeout |
| `Kaspersky:TrustedCertificateThumbprint` | empty | exact SHA-1/SHA-256 thumbprint accepted in addition to platform trust |

## Legacy data

The former employee/case/task handlers are no longer registered and their UI
route is removed. Existing SQLite tables and source types are intentionally not
dropped by this MVP, so installing the read-only replacement cannot destroy
previous lifecycle data. The new hygiene feature adds no database tables.

## Tests

- AD inventory mapping and LDAP contract tests
- KSC OpenAPI login, chunk parsing, limit and authentication-error tests
- name correlation, independent stale rules and version comparison tests
- frontend summary, search/filter and explanatory detail tests
