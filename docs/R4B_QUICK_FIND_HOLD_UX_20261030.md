# R4B — Quick Find, event filtering and independent follow control

Canonical anchor: IEC10X interoperability PRD/ARD TRACE-01, EVENT-01, CAPTURE-01. This UX slice starts from master after PR #65 and does not implement archived .ariec search (separate PR #66).

## Operator behavior
- Frame Trace: a compact search icon, search text and Material-style close/clear icon. Typed filter choices: All frames, TX / SCADA, RX / RTU, Commands, GI, Negative / errors. Case-insensitive text checks Type, COT, CA/IOA, direction/source, meaning, reference and raw HEX. On-screen result count is explicitly scoped to the current bounded 1,200-frame window (or navigated history page), not the whole capture.
- Event Log: retain the existing All/Digital/Analog/Commands/GI category selector; add quick text search and clear. New observed events continue to populate the bounded Event grid even when auto-follow is OFF. Existing Auto icon follows the latest matching event only when enabled.
- Values, Controls and GI cases: use the same minimal search affordance, with row-preserving WPF views rather than a second copied set of rows.
- Text input is debounced on the WPF dispatcher (180 ms), no serial-thread search, no per-frame UI rescans of the entire disk. Filter updates are presentation-only. A user may click the clear icon without clearing capture or evidence.
- Exact Event/Control/GI Open Frame intentionally resets the Trace quick filter to reveal the original; physical source and byte offset matching are untouched.
- Raw passive session history remains available through Older/Newer; this quick filter does not assert a complete-disk content search. For permanent portable .ariec search use the separately owned R3-E workflow.

## Verification
- Unit tests cover case-insensitive matching and TX/RX/command isolation.
- Windows Release build, protocol, xUnit and actual passive WPF smoke and packaging must run at the exact PR HEAD.
- FAT on physical dual-COM RX with continuous class-2 polling, manual scroll, quick filter and frame jumps remains external to synthetic CI.
