# ARIEC60870 v3.4.3 — Protocol Trace Selection Engine Final Fix

## Fixed

### Drag selection direction

Protocol Trace click-drag selection now works both ways:

- top → down
- bottom → up

The selector no longer depends only on the original mouse event source. It now resolves the target row using the realized ListBox item containers and falls back to visual hit testing.

### Shift-click range selection

Shift-click now selects a real range from the anchor row to the clicked row.

Ctrl-click still toggles individual rows.

### Default workspace

Protocol Trace is forced as the default workspace. Evidence Summary is no longer selected during startup.

## Technical note

The selection engine now uses:

- `ItemContainerGenerator.ContainerFromIndex(...)` for realized visible containers,
- row bounds translated to the ListBox coordinate space,
- `VisualTreeHelper.HitTest(...)` as a fast direct-hit path,
- explicit anchor index tracking for Shift/range selection.

## Preserved

- Export selected/visible Protocol Trace to `.txt`
- Save selected trace rows as `.ariec`
- Open `.ariec` capture
- Offline capture review
- Protocol Trace primary workflow
