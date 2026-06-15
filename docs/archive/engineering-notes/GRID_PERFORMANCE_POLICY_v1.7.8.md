# Grid Performance Policy — v1.7.8

## Rules

1. No tooltip on DataGrid rows or cells.
2. Keep row/cell visual triggers only on forensic grids:
   - Operator Evidence
   - Frame Trace
3. Signal List, Value Viewer, Event Log, Diagnostics, and Status History stay plain/light.
4. Keep virtualization and container recycling enabled.
5. Avoid heavy storyboard animations inside grids.
6. Use clickable panels/tooltips outside the grid for explanation.

## Rationale

Industrial protocol tools can color raw monitor lines smoothly when the line monitor is implemented as a lean virtualized/owner-drawn stream. WPF DataGrid can also be smooth, but it must avoid per-cell objects, expensive templates, and excessive bindings.
