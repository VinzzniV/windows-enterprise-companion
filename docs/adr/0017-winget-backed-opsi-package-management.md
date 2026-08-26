# ADR 0017: Winget-backed opsi package management

- **Status:** Accepted
- **Date:** 2026-08-26
- **Deciders:** Vinz
- **Supersedes:** ADR 0014, ADR 0015 and ADR 0016
- **Extends:** ADR 0008

## Context

Manufacturer-page regular expressions, per-product download profiles, pilot gates,
depot synchronization and client rollout duplicated responsibilities already owned
by Winget and opsi. They also made each additional application a custom integration.
The desired boundary is narrower: WEC discovers and maintains depot packages; opsi
remains the system that assigns those packages to clients.

## Decision

1. WEC reads the Winget catalog through the typed
   `Microsoft.Management.Deployment` API. Localized CLI output is never parsed.
2. Initially only source `winget` and machine-wide, SYSTEM-suitable installers are
   accepted. Store, user, Portable, ZIP and Font packages are rejected with a reason.
3. Each managed Winget ID maps to one opsi Product ID on one depot. Adoption of an
   existing Product ID is explicit and previewed. Existing clients and old package
   files are not changed or removed.
4. WEC generates setup, update and uninstall opsi scripts. Setup and update pin the
   exact catalog version; uninstall does not. Setup never pre-uninstalls.
5. The Winget version is the opsi product version. New upstream versions use package
   version `1`; rebuilding the same upstream version increments package version.
   Versions that cannot be represented unchanged by opsi are rejected.
6. Generated sources are uploaded with strict SCP, built and installed on the chosen
   depot. WEC owns only its dedicated workbench subtree and verifies the resulting
   depot version through opsi JSON-RPC.
7. Catalog checks run while WEC is open, are reusable for 24 hours and may be forced.
   Winget outages do not block the read-only opsi dashboard.
8. Every build/check result is audited. Batches continue after an individual failure.
   No Patch Management action writes `productOnClient` or requests setup/update.
9. Manufacturer sources, inventory mappings, vendor HTTP/regex clients, rollout,
   pilot approval, depot synchronization and repository-specific build profiles are
   removed. Existing audit records are retained.

## Consequences

- Most standard software needs no vendor-specific WEC code.
- Depot state and client state remain owned by opsi; WEC stores only management and
  catalog-check metadata.
- Critical or unsuitable software continues through the manual opsi workflow.
- The WEC host is Windows/x64 and requires the Winget deployment COM runtime plus
  Windows OpenSSH client configuration for the target depot.
- Cleanup of old `.opsi` archives and manual workbenches is deliberately separate.
