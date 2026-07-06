# Wec.Modules.Targets

Persists **saved targets** so frequently used servers and clients don't have to
be re-typed (ADR 0010): a print server, opsi server, domain controller, or a
specific client. A saved target is **host + role + optional user name** — it
**never** stores a password (ADR 0007).

Keyed on `(host, role)` (case-insensitive upsert) in the `targets_saved` table.
Roles: `Client`, `PrintServer`, `OpsiServer`, `DomainController`, `Generic`.

## Bridge actions

| Action | Payload | Result |
|---|---|---|
| `targets/list` | `{}` | all saved targets |
| `targets/save` | `{ label, host, role, userName? }` | upsert by (host, role); returns the full list |
| `targets/delete` | `{ id }` | removes one; returns the full list |

The frontend pre-fills each picker by role (`SavedTargetsBar`) and holds the
per-host session credentials in memory only (`TargetContext`), never persisted.

## Tests

Persistence round-trips run against real migrations in
`tests/Wec.Infrastructure.IntegrationTests` (`SavedTargetPersistenceTests`).
