# ARIEC60870 v1.6.9 — Operator UX Readability + IEC-101 Before State Fix

This pass fixes the next usability/proof gap found during IEC-101/104 runtime testing.

## Fixed

- IEC-101/104 Event Log `Before` value no longer stays empty when the engine does not provide an explicit previous value.
  - The desktop layer now keeps a stable last-value cache keyed by protocol/CA/IOA.
  - When a digital spontaneous event arrives, the Event Log uses the cached previous value before updating the Value Viewer.
  - This is especially important for SP/DP validation because the operator needs to see state transition, not only the new state.

## UX redesign

- `Operator Evidence` is now a readable operator summary view instead of a protocol database dump.
- It now defaults to: Time, Dir, Service, Address, Signal, Value, Quality, IED/RTU time, and Meaning.
- Type ID, COT, CA, IOA, FUN/INF, APCI, class, sequence, and raw technical detail remain in `Frame Trace` and the selected-row inspector.
- `Value Viewer` and `Event Log` are also trimmed to compact engineering columns with one combined Address column.
- Raw protocol columns are intentionally hidden in the operator-facing grids to prevent unreadable squeezed columns on normal laptop screens.

## Audit notes

The main protocol truth remains:

- IEC-101/104 process values must be interpreted around Type ID, COT, CA, IOA, quality, and timestamp.
- IEC-101/103 link behaviour still needs ACD/DFC in Frame Trace because these are link-layer forensic signals.
- IEC-104 state-machine validation is still the next major proof pass: STARTDT/STOPDT/TESTFR, N(S)/N(R), t1/t2/t3/k/w, delayed S-frame, and unexpected acknowledgement.
- PUSERTIF-style validation requires state transition proof, COT=3 spontaneous evidence, time-tag delta, acknowledgement timing, and repeated consistency tests.

## Validation performed in sandbox

- XAML XML parse: OK
- C# brace balance: OK
- ZIP integrity: OK

Full .NET build still needs to be run on a Windows machine with .NET SDK / Visual Studio.
