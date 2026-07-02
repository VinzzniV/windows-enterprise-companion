# ADR 0002: Conservative Elevation Strategy

- **Status:** Accepted
- **Date:** 2026-07-02
- **Deciders:** Vinz
- **Supersedes:** —

## Context

Many WEC features require different privilege levels:

| Privilege level | Examples |
|---|---|
| Standard user | Most WMI hardware/OS queries, own event logs, AD reads (domain user) |
| Administrator | Security event log, some WMI security classes, service control, registry HKLM writes, several security checks |
| Domain admin / delegated rights | Parts of Phase 2 (AD analysis) and Phase 4 (remote management) |

Options range from "always run elevated" to "never elevate, degrade silently".
Running permanently as admin is convenient but violates least privilege, triggers
UAC on every start, breaks drag & drop from unelevated Explorer, and makes the
app itself a more attractive attack surface — unacceptable for a tool whose
purpose includes *improving* security posture.

Silent degradation is equally wrong: an admin tool that quietly returns partial
security results produces false confidence.

## Decision

1. **The app starts unelevated by default.** The manifest requests
   `asInvoker`, never `requireAdministrator`.
2. **Privilege detection is a first-class service.** A `IPrivilegeContext`
   service (in `Wec.Core`) reports at runtime: is the process elevated, which
   well-known capabilities are available.
3. **Every check/feature declares its required privilege level** in its
   metadata. Results carry an explicit status, e.g.
   `Succeeded | Failed | RequiresElevation | NotApplicable`.
   The UI renders `RequiresElevation` as a clearly visible state — never as an
   empty result and never as a generic error.
4. **Expected access failures are `Result<T>` failures, not exceptions.**
   `UnauthorizedAccessException` and WMI access-denied HRESULTs are caught at
   the infrastructure boundary and mapped to a typed error
   (`ErrorCode.AccessDenied`) with the required privilege attached.
5. **No elevated helper process in M1.** A separate elevated worker
   (COM elevation moniker or on-demand elevated child process) is the designated
   future mechanism if per-feature elevation becomes necessary. It will get its
   own ADR when a concrete feature demands it.
6. **Manual "Restart as Administrator" is the only elevation path for now.**
   The UI may offer a restart-elevated action (ShellExecute with `runas` verb),
   triggered explicitly by the user, never automatically.

## Alternatives Considered

| Option | Verdict | Reason |
|---|---|---|
| `requireAdministrator` manifest | Rejected | Violates least privilege; UAC on every launch; oversized attack surface |
| Silent degradation without status | Rejected | False confidence in security results |
| Elevated helper process from day 1 | Rejected for M1 | Significant complexity (IPC to elevated process, securing that channel) before any feature needs it — classic premature infrastructure |
| Windows service with SYSTEM rights | Rejected | Even larger attack surface; deployment burden; nothing in Phase 1–3 requires it |

## Consequences

**Positive**

- Least privilege by default; the tool practices what it audits
- Honest results: users always see *why* a check could not run
- Elevation logic is centralized (`IPrivilegeContext`), not scattered per feature

**Negative / accepted risks**

- Some Phase 1 security checks will show `RequiresElevation` in normal use —
  acceptable and by design
- "Restart as Administrator" loses in-memory state — mitigated because state
  lives in SQLite anyway
- A future elevated helper will require a carefully secured IPC channel
  (deferred, own ADR)

**Follow-ups**

- M1 must implement `IPrivilegeContext` and at least one code path returning
  `RequiresElevation` (satisfies the M1 "failure case" requirement)
- Future ADR: elevated helper process design (only when a feature demands it)
