# ADR 0010: Client-Centric Workspace and Saved Targets

- **Status:** Accepted
- **Date:** 2026-07-06
- **Deciders:** Vinz
- **Supersedes:** — (extends ADR 0007 for credential lifetime)

## Context

The per-module pages (Inventory, Security, Diagnostics, …) each repeated the
same "Scan Target" picker and made the admin re-enter credentials on every
page. The mental model in the field is per **client**: "what is going on with
`KF-PC012`?", not "run the inventory tool, then the security tool". Recurring
pain points:

- The same target/credential entry on every tab; nothing tied a client's
  results together.
- The Inventory host list showed the local machine twice (keyed `LOCAL` in the
  UI vs. its machine name in the store).
- Frequently used servers (print server, opsi server, domain controller) had
  to be typed again each session.
- Print Management listed one row per queue with a tall toner chip stack, no
  search, no location grouping, and mixed server- and client-side printers.

## Decision

Pivot the information architecture to be **client-centric**, as a Strangler
migration (build alongside, then flip the default nav — no big-bang rewrite):

1. **Clients workspace** (`/clients`). Clients come from Active Directory
   (`searchComputers`), merged with already-scanned hosts and saved Client
   targets, and are **not** scanned until opened. A client detail
   (`/clients/:host`) runs Inventory / Security / Diagnostics / Printers on
   demand and reuses the existing feature views. Reporting is available per
   client: the executive summary reads whatever inventory/security data was
   already captured for that host (local or remote) and never starts a scan —
   no faked data, just an empty overview until the sections have been run. Two
   clients can be compared (inventory + security diff).

2. **Shared target/credential context.** A React `TargetProvider` holds the
   loaded saved targets and, per host, the explicit credentials entered this
   session. Credentials live in memory for the **app session** (not just one
   request) so a client can be scanned repeatedly without re-typing — they are
   still never persisted and never logged. This is a deliberate, bounded
   extension of ADR 0007's "in memory for the duration of the request";
   persisting credentials (e.g. Windows Credential Manager) would need its own
   ADR.

3. **Saved Targets** (`Wec.Modules.Targets`, table `targets_saved`). A saved
   target stores **host + role + optional user name** — **never a password**.
   Roles: Client / PrintServer / OpsiServer / DomainController / Generic.
   Pickers and the global views pre-fill from them by role.

4. **Navigation** splits into **Clients** (primary) and **Fleet** (Dashboard,
   Active Directory, Patch Management, Print Management) plus **Multi-host**
   (the standalone Inventory/Security/Diagnostics/Reporting pages kept as batch
   runners). Fleet-level views stay global because they are not per-client.

5. **Print Management** is reworked around physical devices: queues that belong
   to one device are merged (serial → IP → base name, stripping the
   `_A5`/`B`/`Black`/`C`/`Color` variant suffix) into one compact, expandable
   row with a multi-segment toner mini-bar; search and site-code grouping are
   added. The merge is display-only aggregation from the snapshot; the
   lease-swap diff stays serial-based. Client-installed printers are a separate
   CIM path (`scanClientPrinters`, `MSFT_Printer`, no SNMP) shown in the client
   detail, not mixed into the print-server view.

## Consequences

- Credentials now outlive a single request (session-scoped, in-memory only).
  The password policy is otherwise unchanged: never persisted, never logged;
  local targets always use the invoking identity.
- The definition/runtime direction is unaffected: this is UI composition plus
  two small backend additions (saved-target persistence, client-printer
  capture). No new workflow special-cases, no free-form technical automation.
- Fleet views remain fully usable throughout; the per-module pages are
  reframed, not removed, so multi-host batch scanning is preserved.
- Remote per-client Reporting remains a known gap (marked NOT_RUN), tracked as
  a follow-up rather than faked.
