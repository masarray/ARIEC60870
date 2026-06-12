# ARIEC60870 v2.7.0 — Adaptive Flush Budget + Dispatcher Self-Diagnostics

## Added

### Adaptive UI flush budget

The UI dispatcher no longer processes a fixed 42 evidence events per tick.

It now raises the flush budget based on queue depth:

- normal: 42 events/tick
- moderate queue: 64 events/tick
- high queue: 96 events/tick
- very high queue: 160 events/tick
- backlog pressure: up to 220 events/tick

This helps the UI catch up during bursty IEC-101/104 sessions without immediately dropping trace data.

### Adaptive backpressure threshold

Backpressure now considers previous UI flush latency:

- normal threshold: `MaxPendingEvidenceBacklog`
- slow UI flush threshold: `MaxPendingEvidenceBacklog / 2`

If the UI is already slow, low-value trace noise is dropped earlier while protected evidence remains preserved.

### Dispatcher self-diagnostics

The UI now raises rate-limited diagnostics when dispatcher health becomes suspicious:

- `ARIEC-UI-QUEUE-PRESSURE`
  - Pending evidence queue reached a high level.
- `ARIEC-UI-SLOW-FLUSH`
  - UI flush cycle exceeded the configured slow-flush threshold.

Buffer status now includes the adaptive budget and flush tick count.

## Preserved

Protected evidence is still not dropped:

- diagnostics,
- warnings/errors,
- digital/process values,
- mapped values,
- GI,
- command ASDUs,
- ACTCON/ACTTERM.

## Why

v2.6.0 added telemetry. v2.7.0 uses that telemetry to adapt the dispatcher instead of only reporting pressure after it happens.
