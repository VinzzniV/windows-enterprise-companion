# ADR 0016: Controlled Custom opsi Package Builds

- **Status:** Superseded by ADR 0017
- **Date:** 2026-08-14
- **Deciders:** Vinz
- **Extends:** ADR 0014 and ADR 0015

## Context

Manufacturer checks identify upstream drift, but custom opsi packages still
require an administrator to download an MSI or EXE, replace the old file in
the workbench, update the product version, build the package and install it.
`opsi-package-updater` cannot perform those provider-specific steps.

Allowing arbitrary commands or script templates from the WebView or database
would turn a package dashboard into an unaudited remote shell. Rebuilding a
package in place would also leave the only workbench copy partially modified
when a download or build fails.

## Decision

1. Custom package automation is opt-in per product through validated
   `PackageAutomationProfiles` configuration. There is no inferred profile.
2. A profile contains an HTTPS release endpoint, a bounded artifact URL regex
   with a mandatory `{version}` placeholder, artifact size limit, absolute
   workbench path, stable installer-relative path and an optional list of exact
   historical installer file names. The placeholder is bound to the previously
   checked manufacturer version, so a changed release fails closed.
3. Infrastructure resolves and downloads the artifact with HTTPS, a bounded
   release response, transfer timeout and maximum artifact size. It computes
   SHA-256 while streaming to a temporary local file.
4. The artifact is uploaded with Windows `scp.exe` using BatchMode, strict
   host-key checking and key/agent authentication. Only generated
   `/tmp/wec-*.artifact` paths are accepted. Temporary local files are removed.
5. The module generates the remote command solely from validated product,
   version and path values. The WebView and persistence cannot supply shell
   fragments. A custom build:
   - copies the workbench into an isolated `/tmp/wec-build-*` directory;
   - writes the artifact under a stable installer name;
   - replaces only configured historical installer names in opsi script files;
   - updates `[Product].version` in `control.toml`;
   - builds with `opsi-makepackage` and installs with `opsi-package-manager`;
   - retains one deterministic approved `.opsi` artifact on the test depot.
6. Preview and explicit confirmation remain mandatory. The original workbench
   is never modified by automation.
7. After a successful pilot and explicit approval, WEC transfers that exact
   approved `.opsi` file from the test depot to each selected depot with
   `scp -3` and installs it there. Production depots do not independently
   resolve or rebuild the release.
8. Download URL, SHA-256, size, remote output, duration and failures are stored
   in the existing package audit entry. A command success is still verified
   against opsi `productOnDepot` state.
9. Greenshot is the first configured profile. Other packages stay on the
   repository-backed ADR 0015 path until an administrator adds and reviews a
   concrete profile.

## Consequences

- Custom packages can follow the same test → pilot → approve → distribute
  workflow without editing a versioned installer name on every release.
- The one-time historical filename list is deliberate: broad script rewriting
  is rejected because it can silently alter unrelated setup logic.
- HTTPS and SHA-256 provide transport evidence, but do not by themselves prove
  publisher identity. Authenticode verification or vendor checksum validation
  remains a follow-up before enabling high-risk third-party packages.
- The opsi server needs `scp`, `sh`, `sed`, `find`, `opsi-makepackage` and
  `opsi-package-manager`, plus sufficient permissions for the configured SSH
  account. As with ADR 0015, WEC never stores an SSH password.

## References

- [opsi custom software integration](https://docs.opsi.org/opsi-docs-en/4.3/clients/linux-client/softwareintegration.html)
- [opsi command-line tools](https://docs.opsi.org/opsi-docs-en/4.3/server/components/commandline.html)
