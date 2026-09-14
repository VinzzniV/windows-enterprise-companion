# Wec.Modules.Microsoft365

Read-only Microsoft Graph source. Infrastructure owns Graph/MSAL; this module
owns the bounded session cache, license capacity calculations, correlation
policy and transport-independent handlers. It references only Wec.Core.

Actions: `getStatus`, `connect`, `disconnect`, `read`, `getContext` under the
`microsoft365` bridge module. `read` accepts an enum and optional object GUID,
never an endpoint, query string, scope, token or arbitrary Graph command.

Cache defaults: ten minutes fresh, one hour retention, 32 query entries.
Expired-but-retained data stays visible until explicit refresh. One gate
serializes source work and coalesces concurrent equivalent successful refreshes.
Caller cancellation reaches the provider; a cancelled leader publishes no
partial cache. Disconnect cancels reads and discards all WEC session evidence.

Graph collections have configurable page/item/response limits. Truncation is
visible; totals are Graph's eventual counts when supplied. Null collections
are errors; an actual empty array is a successful empty result. Read failures
preserve an older snapshot with a typed error. Cached context never invokes Graph.

See ADR 0021 and `docs/microsoft-365-implementation.md` for deployment,
permissions, security boundaries and live acceptance requirements.
