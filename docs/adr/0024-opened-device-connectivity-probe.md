# ADR 0024: Connectivity Probe for an Opened Device

- **Status:** Accepted
- **Date:** 2026-09-28
- **Deciders:** Vinz (explicit choice during Action Center planning)
- **Supersedes:** The explicit-only Ping/WinRM rule in ADRs 0018 and 0022 for
  one exact Windows target when its device Overview is opened

## Context

The Action Center now groups evidence by device. An administrator opening its
Overview needs to see whether that specific Windows target currently answers
Ping or WinRM. Stored source timestamps cannot establish current reachability.

## Decision

1. Opening a device Overview may run one bounded connectivity probe through the
   existing `connectivity/probeHosts` action for its exact operational host.
   The probe is cancelled or ignored when the profile changes. Re-rendering,
   source refresh and tab changes do not start repeated probes.
2. An ambiguous, candidate-only or cloud-only profile has no automatic probe.
   A missing response is reported as "No response", never as proven offline.
   Failures and the time of the last attempt remain visible. The administrator
   may explicitly check again.
3. This exception does not authorize opening a profile to run Inventory,
   Health, Security, a management-source refresh or a multi-host probe.
   Management-source posture appears from cached or stored evidence; fresh
   external reads still require an explicit action.

## Consequences

- Opening an exact Windows device causes one small network operation.
- Tests cover target eligibility, cancellation, response interpretation and
  no repeated probe from ordinary UI updates.
