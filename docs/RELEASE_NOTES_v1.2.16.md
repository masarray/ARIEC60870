# ARIEC60870 v1.2.16 — Side-by-Side Line Monitor Interpreter

## Why this release exists

The previous Frame Trace layout still placed the interpreter under the trace grid. That made the workflow hard to read during commissioning because the user had to scroll between the transaction list, raw bytes, and decoded meaning.

This release changes the Frame Trace page into a true line-monitor workflow:

- TX/RX transaction tracking stays on the left.
- The selected frame interpreter stays on the right.
- Raw hex is visible in the line monitor row and again in grouped form in the interpreter.
- The decoded IEC-103 structure is shown as stable cards, not hover-driven rewriting.

## Main changes

- Replaced the bottom expander interpreter with a permanent right-side frame interpreter.
- Kept the Line Monitor grid focused on transaction tracking: time, direction, class, frame, ASDU/service, signal/address, and meaning.
- Added a right-side Raw Frame section grouped by IEC-103/FT1.2 protocol fields.
- Added a right-side Decoded Frame Content section with structured cards for direction, link layer, ASDU header, FUN/INF, information element, and integrity.
- Removed dependency on hover-driven explanation as the primary workflow.
- Kept Copy raw and Copy decode actions in the interpreter panel.

## Product rule reinforced

ARIEC60870 should follow the IEC_TEST / ASE2000 line-monitor concept, but with a cleaner and more readable engineering workflow:

> traffic on the left, selected frame interpretation on the right, raw evidence visible but not dominant.
