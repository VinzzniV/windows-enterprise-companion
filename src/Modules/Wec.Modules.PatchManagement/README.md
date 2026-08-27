# Wec.Modules.PatchManagement

Patch Management is a Winget-to-opsi packaging workspace. WEC discovers suitable
packages, generates a deterministic opsi workbench and installs the resulting
package on one explicitly selected depot. Client assignment, rollout and scheduling
remain in opsi and cannot be triggered by this module.

## User workflow

The page contains four areas:

- **Overview** reads product and depot versions from opsi and labels products as
  `Winget managed`, `Update available`, `Current` or `Manual`.
- **Clients** reads installed and target versions plus existing opsi action state.
- **Winget packages** searches the typed Winget catalog, previews creation or
  adoption, checks managed packages and builds confirmed updates.
- **History** reads the retained Patch Management audit trail.

Opening **Winget packages** performs a due check. Results are valid for
`WingetCheckInterval` (24 hours by default); **Check now** forces a new query.
Winget failures are displayed locally and do not make the opsi dashboard unusable.

## Eligibility

`IWingetCatalogClient` uses `Microsoft.Management.Deployment` and never parses
localized CLI output. The first version supports only source `winget`, SYSTEM/
machine-wide installers, and excludes Store, user-scoped, Portable, ZIP and Font
packages. A Winget version must consist of decimal components separated by dots so
it can be used unchanged as the opsi product version.

## Package generation

The generated archive contains:

- `OPSI/control.toml` with distinct setup, update and uninstall scripts;
- pinned `winget install` and `winget upgrade` commands;
- an unpinned `winget uninstall` command;
- exact ID/version checks after setup and update;
- a Winget locator helper, changelog and generic icon.

There is no pre-uninstall, fixed installation path or directory deletion. A new
Winget product version starts at opsi package version `1`; rebuilding the same
product version after a template change increments the package version.

Sources are generated in a local temporary directory, archived, uploaded through
strict OpenSSH SCP to `/tmp/wec-winget-<guid>.tar.gz`, built with
`opsi-makepackage` and installed non-interactively with
`opsi-package-manager --quiet -i`. The final workbench
is `<WingetWorkbenchRoot>/<product-id>`. A prior WEC workbench is backed up during
replacement and restored if installation fails. Existing manual workbench paths and
old `.opsi` files are not deleted. The installed depot version is verified through
opsi JSON-RPC before persistence reports success.

## Bridge actions

| Action | Purpose |
|---|---|
| `getConnectionStatus` | Current stored opsi session state |
| `getDashboard` | Read-only opsi product/depot/client overview |
| `listClientStates` | Paged read-only client version states |
| `searchWingetPackages` | Typed catalog search and eligibility result |
| `previewWingetPackage` | Fresh catalog/depot preview for creation or adoption |
| `createOrAdoptWingetPackage` | Confirmed build, install and verification |
| `listManagedWingetPackages` | Persisted management metadata plus live depot version |
| `checkWingetUpdates` | Cached or forced catalog refresh |
| `prepareWingetUpdates` | Fresh common preview of selected updates |
| `applyWingetUpdates` | Confirmed sequential batch; continues after item failure |
| `getAuditLog` | Retained Patch Management history |

Both execution actions reject unconfirmed requests. A changed Winget or depot stand
invalidates the preview. Every write records Product ID, Winget ID in preview data,
depot, old/new versions, result and error. Target client lists remain empty because
the module performs no client action.

`WingetPackageService` remains the public orchestration facade. Catalog lookup,
validation and preview/update planning live in `WingetPackagePlanner`; package
generation, upload, remote installation, depot verification and temporary-file
cleanup live in `WingetPackageExecutor`. Confirmation checks and audit writes stay
in the facade so the side-effect boundary remains explicit.

## Persistence

`patchmanagement_winget_packages` stores the management link and last catalog
check. The actual installed package version is always read live from opsi. Product
ID is unique, as is Winget ID plus depot. The migration drops the former mapping
and manufacturer-source tables but preserves `patchmanagement_audit_entries`.

## Options (`Wec:PatchManagement`)

| Option | Default | Meaning |
|---|---:|---|
| `WingetCheckInterval` | 1 day | Age of a reusable catalog result |
| `WingetRequestTimeout` | 90 seconds | Timeout per typed catalog operation; allows a cold Winget source refresh |
| `WingetWorkbenchRoot` | `/var/lib/opsi/workbench/packages/wec-winget` | Dedicated WEC subdirectory below the opsi package workbench |
| `SshUserName` | empty | Optional OpenSSH/SCP override; empty uses the active opsi username |
| `SshIdentityFile` | empty | Optional private key path |
| `SshConnectTimeout` | 10 seconds | SSH connection timeout |
| `PackageTransferTimeout` | 15 minutes | SCP upload timeout |
| `PackageCommandTimeout` | 30 minutes | Remote build/install timeout |
| `UseNonInteractiveSudo` | `false` | Prefix package commands with `sudo -n` |

## Verification

Backend tests cover dashboard classification, package-version rules, generated
archive/script snapshots, SCP validation, persistence/migrations and bridge timeouts.
Frontend tests cover the two-step adoption flow, batch preview/confirmation, cached
checks, failure isolation and the absence of client-action requests.
