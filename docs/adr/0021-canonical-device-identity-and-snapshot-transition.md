# ADR 0021: Canonical device identity and snapshot transition

- **Status:** Accepted
- **Date:** 2026-09-08
- **Deciders:** Vinz
- **Supersedes:** —

## Context

Client aggregation historically removed every suffix after the first dot. That
merged IPv4 addresses by first octet, merged equal short names from different
DNS domains and could treat a foreign FQDN as the local machine. Inventory
snapshot replacement also used the unqualified host string without a unique
storage constraint. Existing local databases may contain short names, FQDNs,
addresses or duplicates, and their historical evidence must not be deleted or
silently merged during the transition.

## Decision

1. The canonical target identity is the complete normalized hostname, FQDN or
   IP address. Normalization trims surrounding whitespace and a DNS trailing
   dot, canonicalizes IP text and compares case-insensitively. It never removes
   a DNS suffix or any part of an address.
2. A short DNS name is an alias, not a primary key. AD may prove a short-name to
   FQDN alias because both values belong to the same directory object. Other
   source records may use that alias only when it resolves to exactly one
   canonical device in the current snapshot. Ambiguous aliases remain separate;
   WEC does not infer identity from naming convention alone.
3. The local machine is recognized only by its exact machine name or the FQDN
   derived from the local DNS configuration. An unrelated FQDN with the same
   first label remains remote.
4. Client routes continue to carry a hostname or address. Exact identity is
   resolved first; a legacy short-name route may resolve through one proven,
   unambiguous alias. Ambiguous routes are not automatically redirected or
   merged.
5. Inventory persistence adds a nullable normalized `identity_key`. A migration
   backfills only non-empty keys that are unique in the existing database and
   leaves ambiguous duplicate rows untouched with a null key. It never deletes
   or combines historical evidence.
6. New inventory writes use one atomic SQLite upsert per non-null identity key.
   The newest capture timestamp wins, so a late older capture cannot replace a
   newer result. A filtered unique index enforces at most one current row per
   identity. Legacy null-key rows remain readable as transition evidence but
   are not current write targets.

## Alternatives Considered

| Option | Verdict | Reason |
| --- | --- | --- |
| Keep short names as universal keys | Rejected | It cannot distinguish domains or IP addresses and already causes false merges. |
| Resolve every shared first label as an alias | Rejected | The relation is not evidence of identical machines across DNS domains. |
| Delete or merge duplicates in the migration | Rejected | Existing evidence is not sufficient for a safe destructive decision. |
| Add a generic enterprise asset registry | Rejected | M03 needs deterministic correlation, not a new platform or speculative ownership model. |

## Consequences

- Source correlation and frontend merging must retain complete identities and
  apply the same exact-first, unique-alias rule.
- Some formerly merged rows become visibly separate. That is safer than a false
  identity claim and allows an administrator to correct source data explicitly.
- The nullable transition key makes the migration non-destructive while the
  filtered unique index and atomic upsert protect all new current snapshots.
- A future authoritative hardware or directory identifier can extend this rule
  through another ADR; it is not inferred in this slice.
