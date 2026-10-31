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

## 2026-10-30 addendum — MVP tray is a WinForms shell

The MVP tray lives in `src/Beam.Tray` (net8.0-windows + WinForms) purely as a shell to
prove the trigger → fix-sequence → restore wiring end-to-end on real hardware. It holds
**no domain logic**: everything (Night Light, mode, DPI) lives in Beam.Core/Beam.Windows,
and only the orchestration (`SessionManager`, `PresentationManager`) is considered — both
of which are WinUI3-agnostic plain classes usable from the future `Beam.App`.

`Beam.App` (WinUI 3) replaces `Beam.Tray` before shipping. WinForms was chosen here over
WinUI3 purely for speed of the spike; the earlier "WinForms — Too dated for settings UX"
rejection stands for the settings window, not for the throwaway tray host.
