# ARIEC60870 v1.7.5 — Compile Fix

## Fixed

- Removed stale code-behind references to `RailProtocolLabel`, `RailProtocolLine1`, and `RailProtocolLine2`.
- These controls were intentionally removed from the left rail during the v1.7.4 visual de-clutter pass, but the protocol UX refresh method still referenced them.
- The left rail now uses the dynamic product/protocol icon and the main header protocol summary instead of the old text badge.

## Validation

- XAML XML parse: OK.
- C# stale rail references: removed.
- C# brace balance: OK.
- ZIP integrity: OK.
