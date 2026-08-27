# Wec.Modules.Security

Read-only security posture checks with persisted scan history, per host.
Scans run against the local machine or — via WinRM (ADR 0007) — against
remote Windows clients, single or in a parallel batch.

## Bridge actions

| Action | Payload | Result |
|---|---|---|
| `security/runScan` | `{ target?: TargetRequest }` | `SecurityScanResult` — host, process status, findings, per-check outcomes and coverage; persisted |
| `security/runBatchScan` | `{ hosts: string[], userName?, domain?, password? }` | `BatchScanResult` — per-host status/scan/error |
| `security/getLatestScan` | `{ target?: TargetRequest }` | `LatestScanResult` for that host |
| `security/listHosts` | `{}` | Latest persisted scan timestamp per host; no findings/check details |
| `security/getScanHistory` | `{ target?: TargetRequest }` | `ScanHistoryResult` (summaries + diff latest↔previous) for that host |

`TargetRequest` = `{ host?, userName?, domain?, password? }`; empty = local
machine as the current user. During a batch scan the host publishes
`security/batchScanProgress` events (`{ host, status }`) with the per-host
status: `QUEUED → CONNECTING → RUNNING → COMPLETED | COMPLETED_WITH_ERRORS |
FAILED`.

## Checks

| CheckId | Source | Remote-capable |
|---|---|---|
| `WEC-SEC-FIREWALL` | MSFT_NetFirewallProfile | yes |
| `WEC-SEC-DEFENDER` (engine, RTP, signature age) | MSFT_MpComputerStatus | yes |
| `WEC-SEC-SMB1` | Win32_OptionalFeature | yes |
| `WEC-SEC-BITLOCKER` | Win32_EncryptableVolume | yes (local needs elevation) |
| `WEC-SEC-TPM` | Win32_Tpm | yes |
| `WEC-SEC-OSSUPPORT` | Win32_OperatingSystem + offline lifecycle table | yes |
| `WEC-SEC-PATCHLEVEL` | Win32_QuickFixEngineering | yes |
| `WEC-SEC-LOCALADMINS` | Win32_Group/Win32_GroupUser | yes |
| `WEC-SEC-RDP` | registry | **local-only** |
| `WEC-SEC-SECUREBOOT` | registry | **local-only** |
| `WEC-SEC-UAC` | registry | **local-only** |
| `WEC-SEC-REBOOTPENDING` | registry | **local-only** |
| `WEC-SEC-ACCOUNTPOLICY` | SAM modals (`ILocalAccountPolicyReader`) | **local-only** |

Local-only and failed checks do not create informational pseudo-findings.
They produce a persisted per-check execution outcome with a user-safe failure:
`NOT_APPLICABLE` for unsupported remote execution, `REQUIRES_ELEVATION` for
missing privileges and `FAILED` for provider or internal failures. Findings
describe observed conditions only.

## Execution status, coverage and history

- Scan process status, coverage and risk are separate. `COMPLETED` means the
  scan loop ended; it does not mean every applicable check ran or that the
  machine has no findings. Unexpected/provider failures produce
  `COMPLETED_WITH_ERRORS`.
- The result UI preserves that separation. Known complete coverage is presented
  as Availability `Available`, known incomplete coverage as Execution
  `Partial`, and legacy/unknown coverage as Availability `Unknown`; the
  concrete coverage wording remains adjacent to the canonical badge. This
  mapping is shared by the routed Clients Security tab and the reusable result
  context and does not alter stored values.
- Coverage is derived from persisted `SecurityCheckResult` rows. It records the
  total and the counts of `SUCCEEDED`, `FAILED`, `REQUIRES_ELEVATION` and
  `NOT_APPLICABLE`. Coverage is complete only when it is known and every
  applicable check succeeded.
- Finding severity classifies an observed condition. It is never used to encode
  a skipped, failed or privilege-blocked check.
- History compares findings only within the same check and only when that check
  succeeded in both scans. A failed, not-applicable, elevation-blocked or
  missing outcome suppresses both new and resolved claims for that check and is
  listed as uncompared. The diff is fully comparable only when all participating
  checks are comparable.
- Scans stored before coverage version 1 remain `coverage unknown`. The schema
  migration preserves them, but they are not retroactively assumed complete and
  do not produce resolved claims.

## Design notes

- Every check implements `ISecurityCheck` and receives a
  `SecurityScanContext` (target, credentials, connection options).
- BitLocker uses the same narrow `IDiskEncryptionStatusReader` as Inventory.
  Empty or unknown volume results make coverage incomplete; observed
  unprotected volumes remain findings even when another volume is unknown.
- `Win32_GroupUser.PartComponent` parsing handles both nested CIM reference
  instances (class name via `__CLASS`), DMTF reference paths and the observed
  `Win32_UserAccount (Name = "...", Domain = "...")` CIM display form.
  Raw, parsed and unparsed counts are kept separately. Any unparsed reference
  makes the check incomplete; it can never confirm an empty group.
- A crashing check degrades the scan to `COMPLETED_WITH_ERRORS`; the other
  checks keep running.
- Scan history is host-scoped (`security_scans.host`); check outcomes and their
  sanitized failures are persisted with each scan. Batch scans persist one scan
  per reachable host.

## OS lifecycle snapshot

`WEC-SEC-OSSUPPORT` reads `BuildNumber`, `OperatingSystemSKU` and `ProductType`.
The SKU maps the device to Home/Pro, Enterprise/Education, Enterprise LTSC or
IoT Enterprise LTSC; build number alone is never treated as sufficient.
Unmapped SKUs, server product types and unknown build/track combinations return
an incomplete check outcome, not an inferred EOL or supported result.

The offline table is a snapshot dated **2026-08-19**, derived from Microsoft's
official release-health and product lifecycle pages:

- [Windows 11 release information](https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information)
- [Windows 11 Home and Pro lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/windows-11-home-and-pro)
- [Windows 11 Enterprise and Education lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/windows-11-enterprise-and-education)
- [Windows 10 Home and Pro lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/windows-10-home-and-pro)
- [Windows 10 Enterprise and Education lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/windows-10-enterprise-and-education)
- [Windows 10 Enterprise LTSC 2021 lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/windows-10-enterprise-ltsc-2021)
- [Windows IoT Enterprise release history](https://learn.microsoft.com/en-us/windows/iot/iot-enterprise/whats-new/release-history)

To update the snapshot, verify every affected `(build, lifecycle track)` entry
against the Microsoft pages, update `LifecycleSnapshotDate`, and add or adjust
tests for Home/Pro versus Enterprise/Education, Enterprise and IoT LTSC,
unknown SKU/build combinations, and the exact Pacific-time end-of-support
boundary. Do not add a build until its edition/channel mapping and support date
are unambiguous.

## Options (`Wec:Security`)

| Option | Default | Purpose |
|---|---|---|
| `HistoryLimit` | 20 | Scans loaded into the history view |
| `MaxDefenderSignatureAgeDays` | 7 | Signature age that raises a finding |
| `MaxDaysSinceLastInstalledUpdate` | 60 | Patch-level staleness threshold |
| `MinimumPasswordLength` | 8 | Local password-policy baseline |

Batch concurrency comes from `Wec:Remote:MaxParallelScans` (default 4), and
`Wec:Remote:MaxBatchHosts` (default 50) limits one explicitly started batch.

## Tests

`tests/Wec.Modules.Security.Tests` — checks with mocked seams (including
real PartComponent formats), scan service, history/diff, batch error
isolation and progress events.
