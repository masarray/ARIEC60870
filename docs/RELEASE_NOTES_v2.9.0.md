# ARIEC60870 v2.9.0 — Trace Verbosity Governor + Protected Evidence Mode

## Added

### Trace mode selector

A new Protocol Trace mode selector is available in the workspace toolbar:

- `Proof`
- `Balanced`
- `Full`

### Proof mode

Keeps critical evidence and compresses routine raw trace noise:

- protected diagnostics,
- process/digital values,
- mapped values,
- GI milestones,
- command ASDUs,
- ACTCON/ACTTERM,
- warnings/errors.

Routine poll/no-data/ACK/supervisory noise is suppressed from Protocol Trace.

### Balanced mode

Default mode.

- Keeps important raw trace.
- Suppresses supervisory noise.
- Suppresses routine poll/no-data/ACK mostly when Protocol Trace tab is inactive.
- Preserves critical evidence.

### Full mode

Stores all TX/RX rows until ring buffer limit.

## Added telemetry

Buffer status now includes:

- current Trace mode,
- traceSkip count,
- routine suppression count,
- supervisory suppression count.

## Why

v2.8.0 made backpressure auditable. v2.9.0 gives the operator direct control over Protocol Trace retention while still protecting forensic-grade evidence.
