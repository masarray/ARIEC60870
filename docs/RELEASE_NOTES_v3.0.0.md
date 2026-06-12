# ARIEC60870 v3.0.0 — Evidence Retention Policy + Export Integrity Markers

## Added

### Evidence retention policy in Markdown report

Markdown evidence export now appends a dedicated section:

- Protocol Trace mode
- visible/stored ring buffer counts
- trace suppression counters
- low-value compression counters
- dispatcher queue/flush telemetry
- protected evidence policy

### Export integrity header in tab-separated export

Tab-separated grid export now starts with an integrity marker header.

This makes exported grid data self-describing when shared outside the app.

### Export diagnostic marker

Export now emits:

- `ARIEC-EVIDENCE-RETENTION-POLICY`

The marker documents TraceMode, traceSkip, lowValueDropped, qMax, and maxFlush at export time.

## Why

After trace verbosity, ring buffers, and backpressure compression exist, exports must clearly state retention conditions. This prevents compressed routine trace rows from being misread as missing forensic evidence.
