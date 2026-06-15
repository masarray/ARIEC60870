# ARIEC60870 v1.2.2 — Diagnostics Pipeline + Serial Timeout Safety

## Fixed

- Prevented normal serial no-data timeouts from surfacing as unhandled `System.TimeoutException` in WPF/Visual Studio debugging.
- Reworked `SerialByteTransport.ReadAsync` to use a bounded, non-blocking `BytesToRead` polling loop rather than treating `SerialPort.ReadTimeout` as the primary read path.

## Added

- New WPF **Diagnostics** tab.
- Selectable diagnostic rows with copy support.
- Diagnostic detail panel with exception type and stack detail.
- Runtime exception metadata on evidence events.
- Diagnostic appendix in Markdown report.
- Transport exception counter.
- `docs/DIAGNOSTICS_POLICY.md`.

## Product behavior

Recoverable errors now become evidence instead of crashing the workflow:

- serial read exception,
- serial write exception,
- transport close warning,
- master session fault,
- mapping profile load warning,
- timeout/fault warnings.

This keeps ARIEC60870 closer to a professional protocol tester: field faults are diagnosable, selectable, copyable, and reportable.
