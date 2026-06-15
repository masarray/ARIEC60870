# ARIEC60870 v3.6.3 — Messages Filter / Query Manager Foundation

## Added

### Messages Filter Bar

The Messages workspace now includes a compact engineering filter bar:

- text search,
- direction filter,
- engineering preset filter,
- apply,
- clear,
- visible/total count.

### Engineering Presets

Built-in useful IEC protocol presets:

- Negative / NACK
- GI Milestone
- Command Lifecycle
- Digital / Event
- Quality Issue
- Timeout / Error
- ACD / DFC

### MessageRows view projection

Messages now uses `MessageRows`, a filtered projection of the current protocol evidence buffer.

The source of truth remains the protocol evidence buffer:

- Protocol Trace uses `FrameTraceRows`
- Messages uses `MessageRows`, derived from `_protocolTraceStore`
- selected Messages rows still export/capture through the unified evidence workflow

## Improved

### Current Evidence Buffer discipline

Messages is now ready for proper query/filter workflows without splitting the evidence ledger.

### Filtered export

When Messages has no explicit selection, export uses the current filtered visible message set instead of blindly exporting all Protocol Trace rows.

### Auto-scroll with filter

When Auto Scroll Latest is ON, Messages scrolls to the latest visible filtered row.

## Preserved

- Protocol Trace default workspace.
- Compact Messages View.
- Evidence Summary workflow.
- Unified `.ariec` capture.
- Left-rail Auto Scroll Latest.
- Trigger Captures dashboard.
- Clickable panel header collapse UX.
