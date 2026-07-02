# ADR 0001: WebView2 Host in .NET Instead of Tauri

- **Status:** Accepted
- **Date:** 2026-07-02
- **Deciders:** Vinz
- **Supersedes:** —

## Context

Windows Enterprise Companion (WEC) is a long-term, Windows-only desktop application for
enterprise administration. Core functionality depends on Windows-native APIs:

- WMI/CIM queries (hardware, OS, security state)
- Active Directory (System.DirectoryServices, LDAP)
- Windows Event Logs
- PowerShell hosting (System.Management.Automation)
- Registry, services, local security policy

The UI stack is fixed: React, TypeScript, Tailwind CSS.
The original plan proposed Tauri as the desktop shell with a C#/.NET backend.

### Problem with the original plan

Tauri is a Rust framework. A .NET backend can only participate as a **sidecar**:
a second OS process managed by the Tauri shell. This implies:

| Cost | Detail |
|---|---|
| Two runtimes | Rust host + .NET process, both shipped and patched |
| Two toolchains | cargo + dotnet in every build, CI, and dev setup |
| IPC over localhost | Port management, auth token between own processes, serialization layer |
| Process lifecycle | Sidecar crash detection, restart logic, orphan prevention |
| Packaging | Two artifacts bundled, doubled installer complexity |

Tauri's primary benefits are cross-platform support and small binaries.
WEC is Windows-only by definition; cross-platform is not a requirement and never will be.
The binary-size benefit is irrelevant for an internal enterprise tool.

## Decision

WEC is a **single .NET 9 Windows process** hosting the React UI in a
**WebView2** control (Microsoft Edge WebView2, Evergreen Runtime).

- Host: .NET 9 (WinForms or WPF shell — thin, only hosts the WebView2 control)
- UI: React + TypeScript + Tailwind, built with Vite, served from local static assets
- Bridge: `CoreWebView2.PostWebMessageAsJson` / `window.chrome.webview.postMessage`
  with a typed JSON envelope (see ADR 0003, IPC contract — to be written with M1)
- No HTTP server, no open ports, no second process

## Alternatives Considered

| Option | Verdict | Reason |
|---|---|---|
| Tauri (pure Rust backend) | Rejected | Rebuilding WMI/AD/PowerShell interop in Rust; worst possible fit |
| Tauri + .NET sidecar | Rejected | All costs listed above, no offsetting benefit for a Windows-only app |
| ASP.NET Core Kestrel on 127.0.0.1 + WebView2 | Deferred | Viable variant; adds port/token management now. Revisit if Phase 4 (remote management) requires an HTTP surface. The module/handler design must not assume the transport. |
| Blazor Hybrid | Rejected | Discards the chosen React/TypeScript/Tailwind stack |
| WinUI 3 / WPF native UI | Rejected | Discards the web UI stack entirely |

## Consequences

**Positive**

- One process, one runtime, one build pipeline
- Direct, low-latency, typed message bridge; no network stack involved
- Full first-class access to all Windows APIs from the same process
- Fully offline-capable (only local assets are loaded)
- WebView2 Evergreen Runtime is preinstalled on all supported Windows versions

**Negative / accepted risks**

- Windows-only lock-in (explicitly acceptable: the product is Windows-only)
- Dependency on WebView2 runtime presence — mitigated by Evergreen distribution
  and a bootstrapper check at startup
- The message bridge is a custom contract we must design and maintain ourselves
  (mitigated by ADR 0003 and generated TypeScript types)

**Follow-ups**

- ADR 0003: IPC envelope contract (written together with M1)
- Transport-agnostic handler design so a later Kestrel variant stays cheap
