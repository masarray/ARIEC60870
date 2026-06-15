# ARIEC60870 v3.4.6 — Stable Protocol Trace Reading + Evidence Summary Card View

## Improved

### Stable Protocol Trace reading/selection

Protocol Trace no longer keeps visually moving while the user is selecting or reviewing rows.

When Protocol Trace has an active selection, drag selection, or open context menu:

- incoming protocol rows are still stored in the ring buffer,
- evidence/capture engine continues running,
- visible Protocol Trace rendering is held,
- pending visible trace refresh is applied when the user clears selection/resumes live view.

Context menu now includes:

- `Clear Selection / Resume Live Trace`
- `Resume Live Trace`

### Evidence Summary readable card view

Evidence Summary is now rendered as a mono-spaced wrapped card/list view similar to Protocol Trace, instead of a wide table with clipped columns.

Each evidence card shows:

- protocol trace title,
- sequence/time/protocol meta,
- readable meaning,
- signal/value/quality/timestamp strip.

The old Evidence Summary grid remains hidden only as an export backing grid, so existing export logic is preserved.

## Preserved

- Protocol Trace as default workspace
- Lightweight multi-select
- Right-click evidence export
- Save selected `.ariec` capture
- Export selected/visible Protocol Trace `.txt`
- Open capture/offline review
