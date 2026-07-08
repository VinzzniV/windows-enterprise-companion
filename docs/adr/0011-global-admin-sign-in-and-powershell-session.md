# ADR 0011: Global Admin Sign-In and On-Demand PowerShell Session

- **Status:** Accepted
- **Date:** 2026-07-08
- **Deciders:** Vinz
- **Extends:** ADR 0007 (credential lifetime), ADR 0010 (client-centric workspace)

## Context

After ADR 0010 every remote target still asked for credentials per page, and
the app was strictly read-only. Two field needs emerged:

1. **One identity for the session.** The admin wants to sign in once with the
   admin account and have every remote target (Windows scans, AD bind, print
   servers) reuse it — no per-page credential fields.
2. **Act, not just observe.** From the Clients list the admin wants to see who
   is online and, with one click, drop into an interactive PowerShell remoting
   session on that client to actually fix things.

(2) crosses the read-only boundary the app has held since ADR 0006/0007. It is
a limited, **human-driven** execution path (the admin opens a shell and types),
not automated remediation (that remains M6, still ADR-gated).

## Decision

### Global admin sign-in

- A single admin credential is entered once in the top bar (`AdminSignIn`) and
  held in the React `TargetProvider` for the app session only. It is reused for
  every remote target; per-page credential fields are removed (opsi/Patch keeps
  its own login — a separate auth realm).
- The password lives in memory only: **never persisted, never logged** (ADR
  0007 unchanged). Saved Targets still store host + role + user name only.

### One-click PowerShell session

- `system/openPsSession` launches `powershell.exe -NoExit -NoProfile -Command`
  with an `Enter-PSSession -ComputerName <host>` bootstrap in a new console
  window. With the admin signed in it builds a `PSCredential` and connects as
  the admin; otherwise it connects as the current user.
- **Credential handling.** The password is handed to the child process through
  a one-shot environment variable (`WEC_PSS_PW`), never on the command line
  (which other processes can read via `Win32_Process`) and never on disk. The
  bootstrap reads it, builds the `SecureString`, and `Remove-Item`s the
  variable before connecting. The bridge logs only module/action/success — the
  payload (and thus the password) is never logged.
- Host and account are injected as PowerShell single-quoted literals (quotes
  doubled) to avoid command injection.
- Online status feeding this is a credential-free probe: ICMP ping
  ("reachable") + TCP 5985 ("manageable"), only for the visible/filtered rows.

## Consequences

- Credentials are entered once; the whole app acts under one identity for the
  session, matching how admins actually work.
- The app now has exactly one execution path, and it is explicit and
  human-driven — the admin clicks, a real console opens, and every command is
  typed by a person. No automation, no stored password, no elevation of WEC
  itself (ADR 0002 unchanged).
- The password does transit the in-process bridge and the child process memory
  while a session is opened — unavoidable for `Enter-PSSession` with stored
  credentials, and bounded to RAM for the lifetime of the launch.
- Automated remediation (running commands without a human at the console)
  stays out of scope and M6 (its own ADR + decisions first).
