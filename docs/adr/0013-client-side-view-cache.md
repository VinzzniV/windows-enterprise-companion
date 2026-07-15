# ADR 0013: Client-Side View Cache for Fleet Pages

- **Status:** Accepted
- **Date:** 2026-07-15
- **Deciders:** Vinz
- **Supersedes:** —

## Context

Print Management survives a restart: its scans are snapshots in the local
database, so reopening the app shows the fleet immediately. Patch Management and
Active Directory did not. Both opened on an empty form and required re-entering
the same server / domain / user that never changes, plus a fresh scan, before
showing anything.

The obvious symmetry — persist their results server-side too — runs into the
credential rule (ADR 0007, ADR 0008): the opsi password and the admin password
are session-only and **must never be persisted**. So even a perfectly cached
dashboard cannot be *refreshed* automatically on launch; something must still be
re-entered. The question is only whether the user re-enters it while staring at
an empty page or at the data they had last time.

## Decision

Remember each page's **last view** in `localStorage`, keyed `wec.view.<page>`
(`frontend/src/shared/viewCache.ts`). The host runs the frontend from a stable
origin (`https://<virtual host>`, ADR 0001) with a persistent WebView2
user-data folder, so `localStorage` survives a restart — no new table, no
migration, no handler, no bridge round trip.

The stored view *is* the initial React state (lazy `useState` initializer), so a
restart paints the last dashboard with no flash of an empty form.

- **Patch Management** caches `{server, userName, depotFilter, dashboard}`.
- **Active Directory** caches `{form, overview, hygiene}`.

### What this is not

- **Not the source of truth.** The cache is a convenience: every read failure is
  a miss, never an error. Print Management keeps its database snapshots — it has
  history and lease-diff semantics a view cache cannot serve.
- **Not a credential store.** The cached shapes list their fields explicitly;
  `password` is not among them. A test asserts a typed password never reaches
  `localStorage`.

### Restored views are read-only

A cached Patch dashboard renders while disconnected, marked "Stored view from
&lt;time&gt;", with Refresh, the depot filter, rollout preview and package
planning **disabled**. Mapping edits stay enabled (a local-database write) but no
longer trigger a dashboard refresh, which without a session would fail and drop
the restored view. Nothing may act on, or silently re-query, a stale view.

## Consequences

- Reopening either page shows the last data and the connection behind it; only
  the password is missing. Server + user are also auto-saved as Saved Targets
  (ADR 0010) on a successful connect/analyze, so the two survive independently.
- Cached payloads (directory and patch data) sit unencrypted in the WebView2
  user-data folder — the same machine-local trust boundary as the snapshot
  database. AD hygiene includes privileged-group members; this is the admin's own
  machine, and the data was already on disk via the database for print.
- The cache is per-origin: the dev server and the packaged app do not share it.
- `Wec.Modules.ActiveDirectory` still has no persistence layer and needs none.
