# ARIEC60870 v2.5.0 — Runtime Store Finalization

## Added

### Value Store

Value Viewer is now backed by a keyed runtime store:

- `Dictionary<string, ValueRow> _valueRowsByKey`
- UI refresh uses `ReplaceRange(GetSortedValueRowsSnapshot())`
- direct per-row `ValueRows.Add/Remove/Insert` has been removed from the runtime path
- value sorting remains operator-oriented: digital/status first, measurement second, then grouped by IOA/type/signal

### Event Log Ring Store

Relay/Event Log is now backed by:

- `BoundedRingBuffer<RelayEventRow> _relayEventStore`
- filter refresh uses a ring snapshot and `ReplaceRange(...)`
- old `_allRelayEventRows` list has been removed

### Finding and Diagnostic Ring Stores

Findings and diagnostics now have ring-backed runtime stores:

- `BoundedRingBuffer<FindingRow> _findingStore`
- `BoundedRingBuffer<DiagnosticRow> _diagnosticStore`

Visible UI collections are still batch-flushed through `ObservableRangeCollection`.

### Backpressure

The UI dispatcher now has low-value backpressure protection:

- when pending evidence backlog exceeds the configured threshold,
- routine low-value trace events can be dropped,
- diagnostics, digital changes, GI, command events, mapped values, and warnings/errors remain protected.

Buffer status now includes queued and dropped counts.

## Why

v2.4.0 introduced ring buffers for Evidence Summary and Protocol Trace. v2.5.0 extends the same architecture to Value Viewer, Event Log, Findings, and Diagnostics so long-running IEC-101/104 sessions stay bounded, credible, and responsive.
