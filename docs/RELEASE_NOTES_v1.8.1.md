# ARIEC60870 v1.8.1 — Smart Evidence Summary + Mono Protocol Trace

## Changed

### Evidence Summary

`Evidence Summary` is no longer a translated copy of every TX/RX frame. It now behaves as a distilled proof view:

- Includes signal outcomes, digital edge changes, first/significant value proof, quality/timestamp issues, GI milestones, command milestones, and protocol faults.
- Suppresses routine polling noise such as normal Class 1/Class 2 request traffic.
- Suppresses unchanged repeated signal rows by using a per-signal summary signature.
- Keeps full telegram history in `Protocol Trace`.

### Protocol Trace

`Frame Trace` has been renamed to `Protocol Trace` and is styled as a protocol/raw monitor:

- Trace grid uses `Sometype Mono, Cascadia Mono, Consolas` font fallback.
- The trace remains the raw forensic source of truth.
- Interpreter/detail panel continues to show selected frame decoding and linked raw meaning.

## Font Note

The project references `Sometype Mono` via WPF font family fallback. The binary font file is not bundled in this package. If the font is installed on the target machine, it will be used automatically; otherwise the UI falls back to `Cascadia Mono` or `Consolas`.

## Validation

- MainWindow.xaml XML parse: OK
- ModernTheme.xaml XML parse: OK
- C# brace balance: OK
- ZIP integrity: OK
