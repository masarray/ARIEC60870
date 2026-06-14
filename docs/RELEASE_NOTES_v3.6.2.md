# ARIEC60870 v3.6.2 — Messages View + Current Evidence Buffer Discipline

## Added

### Messages View

A new `Messages` workspace provides a compact one-line browser over IEC protocol traffic.

Columns:

- sequence
- time
- direction
- service
- common address
- IOA
- type ID
- COT
- quality
- summary

### Expanded message detail

Selecting a message row opens an expanded decoded detail panel below the compact list.

The detail includes:

- protocol name
- direction
- service
- address
- CA / IOA
- Type ID / COT
- quality
- relay time
- interpreted meaning
- full detail
- raw hex

## Improved

### Current Evidence Buffer discipline

`Messages` reads from the same `FrameTraceRows` current evidence buffer used by `Protocol Trace`.

This creates a cleaner ASE-style split:

- `Messages` = compact browse/search surface
- `Protocol Trace` = detailed reading surface
- `Evidence Summary` = important engineering evidence only

### Auto-scroll behavior

When `Auto scroll latest` is ON, Messages follows the newest row.
When the user selects or right-clicks a message, ARIEC switches to HOLD just like Protocol Trace.

### Export/capture integration

Selected message rows can be exported or captured through the existing Protocol Trace export/capture actions.

## Preserved

- Protocol Trace default workspace.
- Left-rail Auto Scroll Latest.
- Evidence Summary workflow.
- Unified `.ariec` capture.
- Trigger Capture dashboard.
- Clickable panel header collapse UX.
