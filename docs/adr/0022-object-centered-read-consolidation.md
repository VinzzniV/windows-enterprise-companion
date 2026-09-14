# ADR 0022: Object-Centered Read Consolidation

- **Status:** Accepted
- **Date:** 2026-09-14
- **Deciders:** Vinz (explicit autonomous consolidation start)
- **Extends:** ADR 0004, 0006, 0010, 0018, 0019 and 0021
- **Supersedes:** AD-only account scope in ADR 0019 and source-oriented navigation
  placement, only after the preservation gates below pass

## Context

`docs/consolidation-analysis-and-plan.md` identifies address-key collisions,
unscoped identities, fragmented profiles and independently bounded inventories.
The explicit start authorizes its phases 0–7 and 9. Phase 8 collection is excluded.
Existing source records cannot prove one physical device across all sources.

## Decision

1. Devices, Users and Groups become the canonical object workspaces. Adopt the
   grouped navigation in analysis section C2 after feature parity: Work
   (Overview, Action Center), Objects (Devices, Users, Groups), Operations
   (Software & licenses, Vulnerabilities, Print Management, Network Scan,
   Reports), Administration (Data sources, Settings, Error log).
2. Users are accounts, including Entra-anchored accounts with no established AD
   relation. AD remains authoritative for AD account identity and preferred
   contact/organization display. Preserve contradictory cloud fields and
   separate AD/Entra states. Never merge different accounts of one person.
3. Approve bounded search-only AD reads for computer GUID/SID, exact GUID/SID
   resolution and general group identities/direct members. Use the existing
   connection, credential, attribute allowlist, paging and cancellation seams.
   Groups are source-native; direct membership is not effective authorization,
   nested expansion, primary-group reconstruction or hidden-member coverage.
4. References carry object kind, source, authority and native ID. WEC host
   references identify exact stored/execution subjects and remain weak asset
   identities. Normalize full DNS addresses and parsed IPs for comparison;
   keep original values and short aliases. Never truncate identity to a short
   hostname or classify another-domain namesake as the local machine.
5. Confirm only semantically appropriate, unique, scoped IDs: AD GUID/SID,
   Entra object IDs and Intune `azureADDeviceId == Entra deviceId`. Names and
   UPN fallbacks are candidates. Duplicates and conflicting IDs prevent
   automatic selection; candidates never select remote targets or inherit
   another record's compliance, risk or ownership. Preserve all source records.
6. Use existing Windows device evidence. No new registration, BIOS, chassis
   or Windows identity collection is approved. Intune `userId` represents the
   associated user; no primary-user query or new Graph permission is added.
7. Concrete Core read projections are implemented by data-owning modules.
   Clients owns device composition; UserManagement owns accounts;
   GroupManagement owns groups. Extract only actual Client composition from
   EmployeeLifecycle. Leave source adapters, hygiene assessment and frozen
   Lifecycle entities with their current owners. No module-to-module reference,
   universal entity, generic repository or new server is introduced.
8. Profiles and bounded list/search indexes are disposable process/session
   memory. Expose source, scope, retrieval/observation/attempt times, revision,
   coverage and safe errors independently. An unavailable source is not empty
   or healthy; failed refresh may retain same-session stale evidence.
9. Object overview reads cached/stored evidence. Explicit targeted source or
   relationship reads may fetch bounded data. No profile or search keystroke
   triggers a tenant crawl, Windows scan, Ping or WinRM probe. Source owners
   retain query single-flight, cancellation, retention and scope isolation;
   disconnect/context change clears derived cloud evidence and late responses.
10. Filter/sort/page only the declared bounded working set. Preserve source
    totals and truncation separately; no sum of independent pages is called a
    complete environment inventory. No persistent manual links or cloud index.
11. Preserve all functions and deep links in analysis G2, including exports,
    batch cancellation, comparison, Cleanup, AD-only Leaver and existing gated
    Patch/Print writes. Profiles add navigation, not write authority. Keep old
    aliases usable from an empty history. No cloud export expansion.
12. Preserve historical rows without destructive rewrites. MSAL/WAM, GET-only
    Graph, fixed scope allowlist, credentials, elevation and release gates
    remain unchanged. No tag, release or installer publication is authorized.

Nessus identity preservation uses an additive nullable `host_uuid`/`bios_uuid`
mapping of fields the existing importer already reads. Legacy ambiguous
`asset_id` values keep unknown provenance; no automatic backfill or historical
key rewrite occurs. New full-address source keys prevent short-name collisions.

## Alternatives Considered

| Option | Assessment |
| --- | --- |
| Concrete composed profiles and session index | Selected; reuses established owners while preserving provenance. |
| Permanent CMDB identity/override registry | Deferred; needs lifecycle, audit and privacy decisions absent here. |
| Name-based merge or newest enrollment wins | Rejected; destroys conflicting evidence and can select the wrong target. |
| Complete directory/cloud synchronization | Rejected for this scope; bounded reads suffice without new storage governance. |
| Move every adapter or redesign specialist tools | Rejected; unrelated risk without a necessary composition benefit. |

## Consequences

- Source-only profiles are useful even without Windows data. Missing stronger
  device evidence remains visible uncertainty, not an implementation failure.
- Bounded lists do not prove environment-wide absence or uniqueness.
- Implementation follows analysis G1 and verifies G2 before switching sidebar
  entries. Small green local commits precede fully verified milestone pushes.
- Automated identity, mapping, failure, session, cancellation, relationship and
  compatibility tests plus full regression/security checks are required.
  Unavailable company and remote-client acceptance stays explicitly unverified.
