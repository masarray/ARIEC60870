# ARIEC60870 v3.4.2 — Protocol Trace Block Selection + Trace Export UX

## Fixed / Improved

### Protocol Trace block selection

Protocol Trace now supports a more ASE-like line monitor selection workflow:

- click row to select,
- Shift-click to select a range,
- Ctrl-click to toggle a row,
- click-and-drag across rows to select a block.

The drag selection now uses visual hit-testing instead of relying on the original mouse event source, so it works while the ListBox has mouse capture.

### Export selected Protocol Trace

New left-rail action:

- `Trace TXT`

Exports selected Protocol Trace rows to tab-separated `.txt`.

If no rows are selected, it exports all visible Protocol Trace rows.

### Export button behaviour

The generic export action now understands Protocol Trace. When Protocol Trace is active, it exports trace rows instead of showing “not exportable”.

### Diagnostics

New diagnostic marker:

- `ARIEC-TRACE-TXT-EXPORTED`

## Preserved

- Open `.ariec` capture
- Save selected `.ariec` capture
- Offline capture review
- Protocol Trace as primary workspace
- Compact command preview
- True ring buffer runtime
