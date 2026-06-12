# ARIEC60870 v2.5.1 — Backpressure Helper Compile Fix

## Fixed

Resolved compile error:

- `CS0103: IsLowValueBackpressureCandidate does not exist in the current context`

The call site was present in `OnEvidenceReceived(...)`, but the helper method declaration was missing from `MainWindow.xaml.cs`.

## Cleaned

Resolved warning path by making dropped counters visible in BufferStatus:

- `_visibleRelayEventsDropped`
- `_visibleDiagnosticsDropped`

## Preserved

- v2.5.0 runtime store finalization
- Value keyed state store
- Relay event ring buffer
- Finding/diagnostic ring buffers
- Low-value backpressure policy
- No `RemoveAt(0)` in main UI path
