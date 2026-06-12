# ARIEC60870 v2.2.0 — Readable UX + Command Signal Picker

## UX readability

- Long grid cell text is wrapped instead of aggressively ellipsized.
- Evidence Summary, Value Viewer, Findings, Assessment, Diagnostics, and Status History become easier to read directly in-row.
- Horizontal scroll is reduced for main operator grids.
- Long diagnostic/evidence/finding text remains visible without opening detail panel first.

## Command Dock

- Added searchable command signal dropdown in the Command Dock.
- The dropdown is populated from command-capable entries in the loaded IOA database / Signal List.
- Selecting a command signal fills:
  - command type,
  - CA,
  - IOA,
  - setpoint midpoint hint when engineering range is available.
- Manual CA/IOA typing remains available.
- Command Signal detail shows Type, CA, IOA, feedback IOA, and engineering range where available.

## Why

Operators should not need to copy long rows into a detail panel just to read evidence. Command execution also should not depend on memorizing IOA numbers when the database already knows command-capable points.
