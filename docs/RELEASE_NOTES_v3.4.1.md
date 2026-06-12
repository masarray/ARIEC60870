# ARIEC60870 v3.4.1 — Open Capture Workflow Compile Fix

## Fixed

Resolved compile errors introduced by the Protocol Trace drag-select workflow:

- `CS0103`: `_isProtocolTraceDragSelecting` missing field
- `CS0165`: local `listBox` definite-assignment warning in mouse move handler

## Changed

- Added `_isProtocolTraceDragSelecting` field to `MainWindow`.
- Rewrote `FrameTraceGrid_PreviewMouseMove(...)` with explicit sender type guard.
- Rewrote `FrameTraceGrid_PreviewMouseLeftButtonUp(...)` with explicit sender type guard.

## Preserved

- Open `.ariec` capture
- Save selected Protocol Trace block
- Protocol Trace as primary workspace
- Left-rail capture workflow
- Compact command preview
- Drag-select Protocol Trace rows
