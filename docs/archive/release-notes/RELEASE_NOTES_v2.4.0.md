# ARIEC60870 v2.4.0 — True Ring Buffer + UI Batch Dispatcher

## Added

### BoundedRingBuffer

High-volume evidence stores now use fixed-capacity circular buffers:

- `BoundedRingBuffer<EvidenceRow>` for Evidence Summary store.
- `BoundedRingBuffer<EvidenceRow>` for Protocol Trace store.

Appends are O(1), and old rows are overwritten without `RemoveAt(0)` churn.

### ObservableRangeCollection

Visible UI collections now support batch updates:

- `AddRange(...)`
- `ReplaceRange(...)`
- `TrimStart(...)`

WPF receives one Reset notification for each batch operation instead of hundreds of per-row Add/Remove notifications.

### Batched visible row dispatcher

Evidence Summary, Protocol Trace, Findings, and Diagnostics now collect visible rows into pending UI batches and flush them once per UI timer tick.

## Improved

- Eliminated `RemoveAt(0)` from the main window UI path.
- Snapshot refresh now uses `ReplaceRange(...)` from ring buffer snapshots.
- Inactive tab snapshots stay cheap and are refreshed only when needed.
- Protocol Trace and Evidence Summary stay bounded without shifting collection indices per frame.

## Why

High-frequency IEC-101/104 scans can generate thousands of frames. The previous approach throttled events but still updated ObservableCollection one row at a time and trimmed from the front. This release makes the UI path genuinely bounded and batch-oriented.
