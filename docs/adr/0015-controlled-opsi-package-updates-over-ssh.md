# ADR 0015: Controlled opsi Package Updates over OpenSSH

- **Status:** Accepted
- **Date:** 2026-08-14
- **Deciders:** Vinz
- **Extends:** ADR 0008

## Context

ADR 0008 deliberately stopped at an audited `opsi-package-updater` command.
That left package acquisition and cross-depot synchronization outside WEC and
prevented the full test → approve → distribute workflow. opsi 4.3 officially
supports depot-local pull updates with `opsi-package-updater`; push distribution
with `opsi-package-manager` instead requires a concrete `.opsi` archive path,
which WEC does not reliably know.

## Decision

1. WEC runs repository-backed package updates on each selected depot with
   `opsi-package-updater -v update <productId>`. The connected opsi service
   remains the source of truth for depot/product validation and post-command
   version verification.
2. Remote execution lives behind Core `IRemoteCommandExecutor`. Infrastructure
   uses the Windows OpenSSH client (`ssh.exe`) with separate process arguments,
   `BatchMode=yes`, `StrictHostKeyChecking=yes`, a connection timeout and an
   overall command timeout. It never invokes a local shell.
3. Authentication uses the OpenSSH agent/default identities or an optional key
   path. WEC stores no SSH password. Host trust must already exist in the
   Windows user's `known_hosts` file. Optional privilege elevation is
   `sudo -n`, which cannot prompt for a password.
4. Product IDs, depot IDs and the SSH user are validated. The remote command is
   generated solely from validated opsi data; arbitrary commands never cross
   the WebView bridge.
5. Package promotion is server-side gated:
   - update exactly one test depot;
   - deploy to and verify selected pilot clients;
   - explicitly approve the latest successful test-depot update;
   - synchronize the remaining selected depots.
   A later failed test invalidates an older approval. The UI is not the security
   boundary; both preview and execution enforce the gate.
6. Every target depot receives its own audit entry with old/new version, command
   preview, bounded stdout/stderr, duration, result and error. A failed depot
   does not prevent attempts on the remaining depots.
7. `opsi-package-manager` push is not guessed. It can be added later when WEC
   owns a package-build artifact and therefore has a verified `.opsi` path.

## Consequences

- Repository packages can now be updated and synchronized from WEC without a
  stored password and without silent failures.
- Operators must install Windows OpenSSH, pre-trust each depot host key and
  configure key/agent authentication. Non-root accounts also need a narrowly
  scoped non-interactive sudo rule when `UseNonInteractiveSudo` is enabled.
- A successful command is not enough: WEC re-reads `productOnDepot`; an
  unverifiable result is recorded as failed and cannot be promoted.
- Vendor binaries and custom package builds remain provider-specific. The
  manufacturer check identifies upstream drift, while `opsi-package-updater`
  updates packages that are actually available in configured opsi repositories.

## References

- [opsi 4.3 command-line tools](https://docs.opsi.org/opsi-docs-en/4.3/server/components/commandline.html)
- [opsi multi-depot synchronization](https://docs.opsi.org/opsi-docs-en/4.3/opsi-modules/dyndepot.html)
