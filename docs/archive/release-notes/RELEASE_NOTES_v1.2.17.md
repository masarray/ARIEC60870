# ARIEC60870 v1.2.17 — Instrument Cockpit Refactor

## Product direction
ARIEC60870 is now shaped more like a protocol tester/monitor cockpit: the main screen prioritizes TX/RX line monitoring and the selected-frame interpreter, while connection parameters live in a setup dialog.

## Changes
- Replaced the wide configuration sidebar with a compact command rail.
- Moved serial/polling/mapping parameters into a centered **Connection Setup** overlay.
- Replaced large KPI cards with compact instrument chips.
- Added LED-style activity indicators for TX, RX, Class 1, Class 2, events, and diagnostics/findings.
- Session startup now defaults to continuous monitoring: `Session timeout = 0` means run until Stop.
- Preserved the side-by-side line monitor: trace table on the left, frame interpreter on the right.
- Enforced Aptos-only UI typography and removed monospace font usage from XAML.

## Notes
The engine polling policy is unchanged: Class 2 is the normal background cycle, while Class 1 is drained only for ACD=1 or bounded GI follow-up.
