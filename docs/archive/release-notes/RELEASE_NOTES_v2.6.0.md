# ARIEC60870 v2.6.0 — Adaptive Backpressure + Dispatcher Telemetry

## Fixed

### No UI call from protocol event thread

`OnEvidenceReceived(...)` no longer calls `AppendSessionLog(...)` directly during backpressure.  
Backpressure notice is now deferred and emitted from the UI flush cycle.

This avoids cross-thread UI risk during high-volume protocol sessions.

## Improved

### Adaptive backpressure protection

Low-value events are still droppable when queue pressure is extreme, but protected traffic is now stricter:

- diagnostics / warnings / errors,
- mapped values,
- process values,
- digital values,
- GI activity,
- command ASDUs,
- ACTCON / ACTTERM.

Only routine low-value trace noise such as ACK/no-data/background poll lines may be dropped.

### Dispatcher telemetry

Buffer status now reports:

- current queue depth,
- maximum observed queue depth,
- dropped low-value count,
- latest/max UI flush duration,
- evidence/finding processed count,
- visible batch row count.

## Why

v2.5.0 introduced runtime store finalization. v2.6.0 makes the dispatcher safer under real long-running sessions by removing cross-thread UI logging risk and by exposing pressure/latency counters so UI performance can be audited from the app itself.
