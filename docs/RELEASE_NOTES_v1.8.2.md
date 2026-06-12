# ARIEC60870 v1.8.2 — Trace Mono XAML Parse Fix

## Fixed

- Fixed startup `XamlParseException` caused by `TraceMonoFont` StaticResource resolution.
- Protocol Trace now uses direct WPF font-family fallback:
  `Sometype Mono, Cascadia Mono, Consolas`
- Runtime no longer depends on `TraceMonoFont` lookup during `InitializeComponent()`.

## Note

The font file is not bundled. If `Sometype Mono` is installed on the target PC, WPF will use it. Otherwise it falls back to `Cascadia Mono` / `Consolas`.
