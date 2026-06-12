# ARIEC60870 v1.8.9 — Wrapped Protocol Trace Layout

## Changed

- Protocol Trace line monitor now wraps text instead of trimming important data.
- Raw hex evidence is rendered in a compact wrapped mono block.
- Horizontal scrolling is disabled for Protocol Trace.
- Added `ProtocolTraceMeta` for sequence/time/protocol info without polluting the main line.
- Trace line layout is optimized for narrow and wide screens:
  - wrapped title
  - wrapped meaning
  - wrapped raw hex
  - compact row spacing
  - virtualized ListBox remains in use

## Why

A protocol trace must remain readable on laptop screens, external monitors, and narrow dock layouts. The line monitor now keeps the raw evidence visible without forcing the user to scroll sideways.
