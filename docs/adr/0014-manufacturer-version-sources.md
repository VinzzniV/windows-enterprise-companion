# ADR 0014: Configurable Manufacturer Version Sources

- **Status:** Superseded by ADR 0017
- **Date:** 2026-08-14
- **Deciders:** Vinz
- **Extends:** ADR 0008

## Context

opsi reports the versions available on its depots, but it does not know the
latest upstream manufacturer version. A central patch dashboard needs that
comparison without embedding one scraper or API client per vendor. Failures
must be visible and auditable; an unavailable manufacturer site must never be
interpreted as "up to date".

## Decision

1. Each opsi product may have one persisted version source: an HTTPS URL and a
   regular expression whose first capture group is the version.
2. HTTP access lives behind the Core `IVendorVersionClient` seam. The
   Infrastructure implementation enforces HTTPS, a request timeout, a 2 MB
   response limit and a two-second regular-expression timeout.
3. Successful and failed checks persist their timestamp, result, latest known
   version and error. Every check writes a patch-management audit entry with
   the old and new version.
4. Opening the connected dashboard runs checks that are older than
   `ManufacturerCheckInterval` (one day by default). The UI also exposes a
   manual check for one package or all configured sources.
5. No source means `NOT_CONFIGURED`, not an inferred version. A failed source
   means `CHECK_FAILED`, not `CURRENT`.

## Consequences

- Different vendor release pages and simple JSON APIs work without new code.
- Administrators own the extraction pattern and must maintain it when a vendor
  changes its response format.
- The app checks only while it is running and the dashboard is opened. A
  machine-level unattended scheduler remains a separate deployment concern;
  the persisted due time and manual action are ready for such a host later.
- Sources that require authentication or JavaScript execution are intentionally
  unsupported; they need a dedicated provider and a separate credential decision.
