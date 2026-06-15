# ARIEC60870 v3.5.0 — Professional Line Monitor UX

## Added

### Live/Hold toolbar

The main workspace now has a professional line-monitor control strip:

- `LIVE FOLLOW / HOLD` status,
- pending row counter,
- `Follow Live` toggle,
- `Resume` action,
- `Latest` action.

### Follow Live

`Follow Live` controls whether the active evidence workspace should continuously update while communication is active.

When turned off, incoming rows are still captured in the internal stores, but visible Protocol Trace / Evidence Summary is held for stable reading.

### Reading Hold

Protocol Trace and Evidence Summary now use a unified hold model.

The active view is held when:

- Follow Live is off,
- rows are selected,
- user is dragging selection,
- context menu is open.

### Resume

`Resume` clears active selection, turns Follow Live on, syncs the latest stored evidence, and scrolls the active workspace to the latest row.

### Latest

`Latest` syncs the active workspace to the newest stored evidence and scrolls to the latest row without changing Follow Live mode.

## Preserved

- Protocol Trace default workspace.
- Evidence Summary card view.
- Evidence Summary multi-select.
- Protocol Trace multi-select.
- Unified `.ariec` capture.
- Right-click capture export from Protocol Trace and Evidence Summary.
- Single-truth open capture behaviour.
