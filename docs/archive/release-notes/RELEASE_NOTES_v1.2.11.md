# ARIEC60870 v1.2.11 — Hex Map Inspector + Layout Fit

## Focus

This release refines the desktop output for commissioning engineers and protocol inspectors.

## Changes

- Session state card is now more compact and tooltip-backed so long completion messages do not visually clip the header.
- Left setup panel spacing is widened so hover/animated buttons, dropdowns, and text fields no longer appear cut off at the card edge.
- Frame Trace inspector is changed from raw-text dump to a protocol map:
  - left side explains the selected frame in plain commissioning language,
  - right side shows raw hex as hoverable byte/field blocks,
  - hovering a hex block updates the meaning panel and shows a field tooltip.
- The inspector now explains the context of each raw block: start byte, length, control byte, link address, ASDU type, COT, common address, FUN, INF, payload/time/value, checksum, and end byte.
- The raw frame remains available for expert audit, but the default reading path is meaning-first.

## Product rule reinforced

ARIEC60870 must not force ordinary commissioning users to read raw hexadecimal. Raw hex is preserved as audit evidence; the app explains the protocol first.
