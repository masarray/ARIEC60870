# ARIEC60870 v1.2.9 — Premium Compact Header + Readable Frame Trace

## Visual direction

This release tightens the WPF desktop UI to better match the mature engineering software identity:

- smaller, calmer header typography;
- lighter font weights instead of heavy semi-bold UI text;
- compact KPI cards with subtle premium gradient surfaces;
- reduced decorative/noisy information density in the top area;
- better frame-trace grid focused on commissioning/interoperability meaning first;
- raw hex remains available in the selected-frame inspector instead of consuming primary grid width.

## Frame trace output

The Frame Trace tab now shows only the fields that are useful for commissioning engineers and interoperability inspectors:

- sequence;
- time;
- direction;
- class;
- ASDU;
- signal/address;
- relay timestamp;
- readable meaning.

Full raw hex is still preserved and shown in the lower selected-frame panel for expert audit and protocol transparency.

## Typography and card polish

- Main heading reduced to a more mature product size.
- Session state card is more compact.
- KPI cards use lighter, sharper typography.
- Cards use a very subtle blue-tinted gradient to feel alive without becoming decorative noise.

## Guardrail

Primary UI views must explain protocol meaning first. Raw hex is evidence, not the main language of the app.
