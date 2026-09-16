# ADR 0021: Microsoft 365 as a Read-Only Administrative Source

- **Status:** Accepted
- **Date:** 2026-09-14
- **Deciders:** Vinz (explicit Microsoft 365 implementation request)
- **Extends:** ADR 0004, 0007, 0018 and 0019

## Context

WEC needs tenant, Entra user/group/device, licensing and optional Intune and
authentication-registration evidence. The user explicitly authorizes this new
external source and the listed administrative personal-data fields. AD remains
authoritative for existing User 360 identities. Microsoft 365 is not another
employee database. No mailbox content, authentication secrets or sign-in event
history is authorized.

## Decision

1. Add `Wec.Modules.Microsoft365` and `frontend/src/features/microsoft365`.
   Core owns concrete read contracts. Infrastructure owns Graph SDK models,
   transport and MSAL. Host wires the module, options and native window handle.
   Do not add generic repositories, provider frameworks or a second process.
2. Use a tenant-specific public-client registration and delegated authentication
   through MSAL.NET and the Windows Web Account Manager (WAM). Authentication
   is an explicit action, separate from Windows scan credentials. No password,
   client secret, certificate private key or token crosses the WEC bridge.
3. MSAL manages token acquisition and renewal. WEC retains its MSAL cache in
   memory only; WAM owns OS-protected broker storage. No custom token encryption
   or serialization. Disconnect discards WEC session/cache, not the Windows
   account or the tenant's consent. Unavailable WAM fails visibly; browser
   fallback is disabled to preserve the no-listening-port boundary.
4. Request only fixed read scopes for enabled features. Intune and authentication
   reports are opt-in. Never request `.default`, `Directory.Read.All`, write
   scopes or application permissions. Application/certificate authentication
   requires a later decision; a desktop public client cannot protect a shared
   application credential from its operator.
5. Graph v1.0 only, public Microsoft cloud only. Hardcoded trusted authority and
   Graph hosts are security boundaries, not configurable endpoints. Enforce
   HTTPS, expected host/path and GET-only transport before attaching a token.
   Validate continuation links, disable redirects and bound pages, items,
   cache entries, retries, total query lifetime and response size.
6. Use process-local, session-isolated cache entries per concrete query.
   Navigation may reuse cached queries; Client/User 360 correlation reads only
   already cached evidence. Refresh is explicit, single-flight and cancellable.
   Failed refresh preserves the old snapshot with an error and stale state.
   No Graph DTO, personal data or token is stored in SQLite/localStorage.
7. Preserve nulls and distinguish unavailable, empty, incomplete and stale.
   Feature-specific 403 errors do not turn into empty inventories. Granted
   token scopes do not prove tenant role, licensing or endpoint access.
8. Correlate AD users by exact SID against `onPremisesSecurityIdentifier`.
   UPN matches are only candidates; never use display names or assume that AD
   objectGUID is the configured Entra sourceAnchor. Correlate Intune to Entra
   using `azureADDeviceId == deviceId`, rejecting empty/all-zero IDs and
   ambiguous matches. Existing WEC snapshots lack a stable Entra device ID;
   exact names may be presented only as candidate evidence, never a confirmed
   join or ownership claim. Hardware serials are not present in the existing
   WEC projection, so this version does not invent a serial-based relationship.
9. Keep cloud detail navigation in the existing application. User 360 and
   Client 360 receive source-labelled, timestamped correlation context without
   triggering Graph, AD or remote scans on open.
10. MFA registration/capability is not MFA enforcement or Conditional Access.
    `mail` is an address, not proof that an Exchange mailbox exists. Graph SKU
    identifiers/names remain authoritative; no guessed marketing-name map.
    Missing fields and unsupported features remain explicit.

## Alternatives Considered

| Option | Assessment |
| --- | --- |
| Delegated WAM | Selected: user and tenant policy apply; OS broker supports enterprise authentication without a WEC listener. |
| Interactive system browser | Supported by MSAL, but desktop loopback redirect needs a listener, conflicting with ADR 0001. |
| Device code | No listener, but phishing exposure and Conditional Access restrictions make it an inferior default for this Windows UI. No automatic fallback. |
| Application permissions with certificate | Suitable for separately governed unattended workloads; too much tenant-wide authority on an administrator's desktop for this version. |
| Client secret / password flow | Rejected: public clients cannot keep shared secrets; password flow undermines modern authentication. |
| Persist complete Graph inventories | Rejected: no established offline-history need; duplicates personal data and introduces retention/migration obligations. |

## Consequences

- A tenant administrator must register WEC, configure the WAM redirect and
  grant the selected permissions. No real tenant is required for automated tests.
- Live WAM, Conditional Access, consent and tenant role/license acceptance are
  company-environment gates, not claimed by fixture tests.
- Bounded inventories can be incomplete in large tenants; the UI must state
  that counts/search describe the loaded subset. Delta sync and persistent
  indexing require a measured need before implementation.
- A compromised Windows user session can misuse delegated access. WEC's
  read-only boundary limits accidental writes but does not secure a compromised
  endpoint. Tenant app assignment, least-privileged roles and Conditional Access
  remain deployment responsibilities.
