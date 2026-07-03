# Wec.Modules.Security

Read-only security posture checks with persisted scan history, per host.
Scans run against the local machine or — via WinRM (ADR 0007) — against
remote Windows clients, single or in a parallel batch.

## Bridge actions

| Action | Payload | Result |
|---|---|---|
| `security/runScan` | `{ target?: TargetRequest }` | `SecurityScanResult` — host, status, findings; persisted |
| `security/runBatchScan` | `{ hosts: string[], userName?, domain?, password? }` | `BatchScanResult` — per-host status/scan/error |
| `security/getLatestScan` | `{ target?: TargetRequest }` | `LatestScanResult` for that host |
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

Local-only checks produce a visible `…-LOCAL-ONLY` INFO finding on remote
targets instead of silently reading the wrong machine (ADR 0007). A check
that could not run produces a `…-NOT-RUN` INFO finding carrying the error —
never silence (ADR 0002).

## Design notes

- Every check implements `ISecurityCheck` and receives a
  `SecurityScanContext` (target, credentials, connection options).
- `Win32_GroupUser.PartComponent` parsing handles both nested CIM reference
  instances (what MMI returns; class name via `__CLASS`) and DMTF reference
  strings — members are shown with scope (local/domain/built-in) and kind
  (user/group/account).
- A crashing check degrades the scan to `COMPLETED_WITH_ERRORS`; the other
  checks keep running.
- Scan history is host-scoped (`security_scans.host`); batch scans persist
  one scan per reachable host.

## Options (`Wec:Security`)

| Option | Default | Purpose |
|---|---|---|
| `HistoryLimit` | 20 | Scans loaded into the history view |
| `MaxDefenderSignatureAgeDays` | 7 | Signature age that raises a finding |
| `MaxDaysSinceLastInstalledUpdate` | 60 | Patch-level staleness threshold |
| `MinimumPasswordLength` | 8 | Local password-policy baseline |

Batch concurrency comes from `Wec:Remote:MaxParallelScans` (default 4).

## Tests

`tests/Wec.Modules.Security.Tests` — checks with mocked seams (including
real PartComponent formats), scan service, history/diff, batch error
isolation and progress events.
