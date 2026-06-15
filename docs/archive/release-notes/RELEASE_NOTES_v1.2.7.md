# ARIEC60870 v1.2.7 — Commissioning Output Fit

## Fixed

- Reduced Frame Trace noise: the expert trace now shows only real TX/RX frames with raw hex.
- Empty raw-hex STATE rows are no longer pushed into Frame Trace.
- Operator Evidence now suppresses repetitive normal Class 2 polling noise and keeps meaningful events, warnings, relay values, GI, ACD, timeout, reset, and event-drain activity.
- Reworked key grids to avoid routine horizontal scrolling:
  - Operator Evidence
  - Value Viewer
  - Event Log
  - AutoTest Assessment
  - Findings
  - Diagnostics
- Raw hex is still available, but as selected-row evidence and expert Frame Trace content, not as the primary operator language.
- Improved TextBox vertical alignment and disabled-state contrast so connection fields remain readable.

## Product policy

ARIEC60870 must explain protocol behavior first. Raw hex stays available for transparency, but commissioning engineers should see meaning, action, signal/value, relay timestamp, and evidence without fighting wide grids.
