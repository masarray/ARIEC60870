# ARIEC60870 v1.2.15 — No-Scroll Line Monitor Reader

## Focus
This release improves the Line Monitor Pro layout so commissioning engineers can read the selected IEC-103 frame without scrolling inside the interpreter panel.

## Changes
- Added a compact `Frame` column to the Line Monitor grid so each row still exposes the raw hex evidence at a glance.
- Reworked the selected-frame interpreter into compact protocol cards.
- Removed nested interpreter scroll areas; the selected frame explanation is now laid out as wrap cards.
- Kept raw bytes grouped by protocol field, not by single byte.
- Maintained synchronized hover/click highlight between protocol meaning cards and raw hex groups.

## UX rule locked
The line monitor must show raw evidence, but the workflow is the protocol interpretation. Raw hex stays visible; IEC-103 meaning must be readable without forcing routine vertical scrolling inside the inspector.
