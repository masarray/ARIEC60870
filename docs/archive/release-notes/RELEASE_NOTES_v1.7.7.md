# ARIEC60870 v1.7.7 — Grid Responsiveness + Tactile Segmented Navigation

## Fixed / improved

- Signal List workspace is capped to a fast visible snapshot in the main workspace. The full database remains available through Edit Signal List.
- Removed global per-cell tooltip creation from grid cell styles to reduce visual-tree/object churn.
- Removed global `TrafficTone` binding triggers from every DataGridCell. Traffic colouring is now applied only to Operator Evidence and Frame Trace.
- TX/RX/timeout colour now propagates through all readable cells in traffic grids by making text styles inherit the DataGridCell foreground.
- Timeout/fault rows render red in the traffic grids.
- Segmented navigation no longer animates a moving slider. Each segment is now a tactile pill with hover grow and click shrink behaviour.
- UI flush caps were reduced to keep the dispatcher responsive when many protocol rows arrive quickly.

## Why this pass matters

The previous pass still made tab switching feel heavy because heavy grid styles and trigger bindings were applied globally. This pass scopes the expensive visual logic to the two grids that actually need traffic-tone semantics and keeps mapping/database grids as plain virtualized tables.
