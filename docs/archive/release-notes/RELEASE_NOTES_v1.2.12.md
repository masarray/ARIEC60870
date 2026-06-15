# ARIEC60870 v1.2.12 — ASE-Style Hex Group Map + Sidebar Safe Margins

## Fixed

- Reworked the Frame Trace inspector so raw hex hover no longer replaces the meaning panel text and causes visual flicker.
- Raw hex is now grouped by protocol block instead of isolated bytes:
  - FT1.2 envelope / length block
  - Control + link address
  - ASDU header
  - FUN/INF signal address
  - Payload / value / relay time
  - Checksum + end byte
- Hovering a raw-hex group now highlights the corresponding protocol meaning row, similar to classic protocol line-monitor behavior.
- The protocol meaning map remains stable while only the relevant block is highlighted.
- Added safe internal margins to the left command panel and button chrome so hover/shadow effects are not clipped by the card edge.

## Product rule reinforced

ARIEC60870 should explain the IEC-103 protocol first and expose raw hex as structured audit evidence. It must not make users interpret single hex bytes manually unless they explicitly inspect expert-level details.
