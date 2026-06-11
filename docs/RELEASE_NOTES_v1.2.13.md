# ARIEC60870 v1.2.13 — Line Monitor Pro Foundation

## Focus

This release changes the Frame Trace inspector from a generic table/detail view into a protocol-aware line monitor inspired by field protocol tools, but with a cleaner ARIEC60870 product workflow.

## Changes

- Frame Trace now has a dedicated **Line Monitor Pro** inspector.
- Selected TX/RX frame shows a stable direction title: master-to-relay or relay-to-master.
- Raw hex is grouped by IEC-103 structure instead of explained as isolated bytes:
  - FT1.2 envelope / length
  - control + link address
  - ASDU header
  - FUN/INF object address
  - information element / value / relay time
  - checksum + end byte
- Interpreter panel mirrors the raw groups so hover/click on raw bytes highlights the matching IEC-103 meaning.
- Hovering interpreter rows also highlights the matching raw byte group.
- Click any raw or meaning group to pin the highlight; use Clear highlight to release it.
- Added Copy raw and Copy decode actions for the selected frame.

## Product rule

Raw bytes remain visible for audit, but the primary user workflow is protocol meaning and commissioning diagnosis. The inspector must stay stable; hover must never rewrite the explanation panel or cause layout flicker.
