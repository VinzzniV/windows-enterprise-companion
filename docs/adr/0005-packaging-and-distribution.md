# ADR 0005: Packaging and Distribution

- **Status:** Accepted
- **Date:** 2026-07-02
- **Deciders:** Vinz
- **Supersedes:** —

## Context

M8 must turn the CI publish output into something an administrator can put on
a machine. The decision space has three independent axes — runtime model,
distribution format, and trust/updates — and whatever we pick also unblocks
the deferred release half of M9 (tags, release artifacts).

Constraints that shaped the decision:

- **Target machines are arbitrary enterprise Windows 11 clients.** The .NET
  Desktop Runtime cannot be assumed to be present or current on them; the
  WebView2 Evergreen Runtime *can* (preinstalled on all current Windows 11
  builds, kept current by Windows Update).
- **Single developer, no code-signing certificate.** Anything that hard
  requires signing (MSIX) is currently not buildable.
- **ADR 0002 (asInvoker, never elevate)** should extend to installation:
  an admin tool that needs admin rights just to *install* for the current
  user would be inconsistent.
- Measured publish sizes (Release, including frontend assets):
  framework-dependent ≈ 39 MB, self-contained win-x64 ≈ 180 MB.

## Decision

1. **Runtime model: self-contained, `win-x64`.** The ~180 MB cost buys zero
   prerequisites on target machines. The flip side is accepted consciously:
   .NET runtime security patches now require a WEC re-release — with CI in
   place that is one rebuild. The runtime shipped is **.NET 10 LTS**
   (upgraded from the EOL .NET 9 STS in the same milestone, before the
   runtime got baked into an artifact).
2. **Distribution format A: portable ZIP, built by CI.** A versioned
   `wec-<version>-win-x64.zip` of the self-contained publish output is the
   canonical artifact — typical for admin tooling (run from a tools share,
   no installation footprint beyond `%LOCALAPPDATA%\Wec` at runtime).
3. **Distribution format B: Inno Setup per-user installer.** Installs to
   `%LOCALAPPDATA%\Programs\Wec` **without administrator rights**
   (`PrivilegesRequired=lowest`), provides a Start menu entry and a clean
   uninstaller. Built in CI alongside the ZIP.
4. **WebView2: assume Evergreen, detect at startup.** No bootstrapper is
   bundled; the host already shows an actionable error dialog when the
   runtime is missing (`WebView2RuntimeNotFoundException` handler).
5. **Explicitly out of scope** (revisit when the constraint changes, each
   would need a new ADR or an update to this one):
   - **Code signing / MSIX** — no certificate. Consequence: SmartScreen
     warns on first run of downloaded artifacts. Documented as a known
     limitation in the README. MSIX (winget, Intune) becomes attractive the
     moment a certificate exists.
   - **Auto-update** — premature before real deployment experience exists.
   - **Trimming / NativeAOT** — WinForms, EF Core and WMI rely on
     reflection; unsupported or high-risk for zero user value at this size.
   - **WiX MSI (per-machine, GPO deployment)** — real enterprise scenario,
     but wrong for the current single-admin usage; per-machine install also
     requires elevation.

## Consequences

- CI produces two artifacts per green master build: the portable ZIP and
  the per-user installer. The M9 release process can attach both to tagged
  GitHub releases.
- Publish for packaging uses `--runtime win-x64 --self-contained true` and
  therefore cannot reuse the framework-dependent build output (`--no-build`
  does not apply; the packaging publish compiles for the RID).
- Runtime patching duty moves from the target machine to this repository:
  dependabot-style version bumps of the SDK pin (`global.json`) become a
  maintenance routine.
- The unsigned-binary SmartScreen warning stays until a certificate is
  budgeted; per-user install keeps the blast radius of that trade-off small.
