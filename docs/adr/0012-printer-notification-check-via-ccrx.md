# ADR 0012: Printer Notification-Config Check via Command Center RX

- **Status:** Accepted
- **Date:** 2026-07-08
- **Deciders:** Vinz
- **Supersedes:** —

## Context

The external print service provider (MPS) is only alerted about a device
(e.g. low toner) if the printer itself is configured to e-mail the event.
Operations needs to spot printers where that notification is **not**
configured, so a device silently stops reporting. The required state, per
the fleet's convention:

1. **SMTP is on** and points at the correct mail server.
2. The device has a **sender e-mail address**.
3. A **scheduled event report** sends the *low-toner* event to a recipient
   (the MPS provider, e.g. `mps@compend.de`).

The fleet is uniformly UTAX/Kyocera on **Command Center RX** (the 2011
"KYOCERA MITA" generation, a Knockout.js SPA), factory-default admin
`Admin`/`Admin`.

We evaluated **SNMP first** (ADR 0009 already reads the devices over SNMP).
The Kyocera private MIB (`1.3.6.1.4.1.1347`) exposes only hardware / status /
counters — **no** SMTP, sender or event-report objects. SNMP cannot answer
the question. Confirmed by reading the MIB; documented so we don't re-litigate.

## Decision

Read the settings over **HTTPS from the Command Center RX**, the same source
the admin uses in a browser. Reverse-engineered protocol:

- Self-signed cert → trust it. A browser `User-Agent` + `Referer` are required
  or the embedded server returns HTTP 500.
- `GET /` establishes a session cookie.
- Login is a **plain form POST** to `/startwlm/login.cgi` (no hash/challenge):
  `func=authLogin`, `arg01_UserName`, `arg02_Password`, `arg03_LoginType=_mode_off`,
  `arg04_LoginFrom=_wlm_login`. Success sets a `level=1` cookie.
- Settings live in `*.model.htm` files as `_pp.<name> = '<value>'` assignments —
  parsed with a regex. Without login these return a ~1115-byte stub (0 properties).
  - E-mail/SMTP: `/js/jssrc/model/funcset/email/FuncSet_Email_EmailSet.model.htm`
    (`smtpMode`, `sndSmtpMode`, `smtpServerName`, `senderAddress`).
  - Notification/report: `/js/jssrc/model/mngset/nfcrptset/MngSet_NfcRpt_NfcRptSet.model.htm`
    (`evtSch{1..3}Address`, `evtSch{1..3}LowToner`).

### Architecture

- Core seam `ICcrxClient` + pure `CcrxModelParser` (`Wec.Core.Ccrx`), mirroring
  `ISnmpReader`. Infrastructure impl `CcrxHttpClient` (session → login → read).
  **Read-only**: no write endpoint, WEC never changes a printer's config.
- `NotificationConfigService` (PrintManagement) evaluates the three rules; the
  handler `printmanagement/checkNotificationConfig` runs one device on demand.
- Expected mail server / recipient are configurable
  (`Wec:PrintManagement:ExpectedSmtpServer` with a `{site}` token,
  `ExpectedEventRecipient`). Unset = only require non-empty.

### Credentials (ADR 0007 unchanged)

The CCRX admin password is a **session-only** input, never persisted, never
logged (the dispatcher already omits payloads from logs). Omitted = the factory
default `Admin`/`Admin` the fleet ships on. It is its own auth realm, separate
from the Windows/global admin and from opsi.

## Consequences

- The check is **on-demand per device** (HTTPS round trips: session + login +
  two GETs), not part of every scan.
- Devices that are unreachable, not CCRX, or refuse the login are reported as
  **NOT_CHECKED** — never as a false "all clear".
- The protocol is **undocumented and firmware-dependent**. Model paths / property
  names were verified against the fleet's generation; a materially different
  firmware may need adjustment. Fragility is contained to `CcrxHttpClient` +
  `NotificationConfigService`; the parser and rules are unit-tested.
