# ADR-0003 — .NET 9 + WinUI 3 + CsWin32, packaged as MSIX

**Status:** Accepted
**Date:** 2026-10-29

## Context

Requirements: tray app, small settings window, toasts, native Win32 calls, unit-testable
core, upstream compatibility with PowerToys (C#/.NET + C++/WinRT, WinUI 3 settings).

## Decision

- **Language/runtime:** C# 13 on .NET 9 (move to .NET 10 when PowerToys does).
- **UI:** WinUI 3 (Windows App SDK) for settings/diagnostics; tray via Win32 interop
  (`Shell_NotifyIcon`) or a thin library.
- **Interop:** CsWin32 source generator for `user32`/`gdi32`/`setupapi`; no hand-written
  `DllImport` in domain code.
- **Packaging:** MSIX for v1 (startup task, toasts, clean install/uninstall); unpackaged
  debug builds for spikes.
- **Tests:** xUnit + FluentAssertions; hand-written fakes.

## Consequences

**Positive**
- Same stack as PowerToys -> upstream port is mostly moving files.
- CsWin32 gives safe structs and constants without a native layer.
- MSIX gives identity for toasts and a startup task without admin.

**Negative**
- WinUI 3 adds ~40–60 MB to the install and some startup cost.
- MSIX complicates rapid dev loops (solved with unpackaged debug).
- WinUI 3 has quirks on multi-monitor DPI (settings window only, not the core).

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| C++/WinRT | Faster/smaller, but slower to build the domain and tests; upstream PowerDisplay is C# |
| WPF | Mature, but PowerToys settings are WinUI 3; would need a rewrite upstream |
| WinForms | Too dated for settings UX; accessibility work |
| Rust + Tauri | Excellent core, but no PowerToys path, extra toolchain |
| Avalonia | Cross-platform not needed; extra dependency |

## Notes

If startup footprint becomes a problem, split into a tiny native tray host + .NET core
loaded on demand. Not justified now.

## 2026-11-01 addendum — shipped 0.1.0: net10.0, WinForms tray, Inno Setup, Beam.App

The original addendum (2026-10-30) was aspirational. Here is what actually shipped:

- **Runtime:** .NET 10 (net10.0-windows for Tray/Windows; net10.0 for Core/App; no net8.0).
- **Tray:** `Beam.Tray` is WinForms, not WinUI 3, and is the permanent composition root for
  the MVP. It is thin (no domain logic), and the plan to replace it with a WinUI 3 host
  remains a Phase 2/3 idea, not the 0.1.0 deliverable.
- **App layer:** `Beam.App` (net10.0) exists as a separate project between Core and Windows,
  not as a WinUI 3 app. Its `PresentationManager` and `SessionManager` are plain classes that
  can be hosted by WinForms, a CLI or a future WinUI 3 shell. This is ADR-0014.
- **Packaging:** Inno Setup (MSIX was planned but deferred; see the roadmap). The tray
  registers itself in the user's `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` key
  for startup; no admin, no MSIX.
- **CLI:** `Beam.Cli` is a composition root that wires the same adapters as the tray; it is
  a one-shot diagnostic tool, not a pipe client of the tray process.
- **Ports:** `Beam.Core.Ports` defines five interfaces that `Beam.App` depends on;
  `Beam.Architecture.Tests` enforces that neither Core nor App can see WinForms or P/Invoke.
- **No upstream changes yet:** the PowerToys donation path (ADR-0001) is still the long-term
  goal; the liftable unit is now `Beam.Core + Beam.App` rather than `Beam.Core +
  Beam.Windows`.

