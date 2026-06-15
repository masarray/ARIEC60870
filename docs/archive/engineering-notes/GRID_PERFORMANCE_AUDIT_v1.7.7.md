# Grid Performance Audit v1.7.7

## Root cause found

The app had correct virtualization flags, but the visual layer still did too much work:

1. Every DataGridCell globally evaluated `TrafficTone` triggers, even in Value Viewer, Signal List, Event Log, Diagnostics, and mapping tables that do not have a `TrafficTone` property.
2. Every grid cell text style automatically created a tooltip from its own text.
3. Signal List rendered the full mapping database in the main workspace.
4. The segmented navigation used a moving slider animation even while DataGrid layout work was active.

## Corrective action

- Scope traffic-tone cell style only to Operator Evidence and Frame Trace.
- Remove global per-cell tooltip creation.
- Limit main Signal List view to a visible snapshot; full editing stays in Signal List Editor.
- Replace segmented slider animation with per-button tactile grow/shrink.
- Reduce dispatcher flush count per tick.

## Remaining future work

- Add search/filter/paging to Signal List workspace.
- Add optional column chooser for Operator Evidence and Frame Trace.
- Add performance counters for UI queue depth and effective render latency.
