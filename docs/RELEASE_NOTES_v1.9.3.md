# ARIEC60870 v1.9.3 — GI Negative Confirmation Fallback Fix

## Fixed

The v1.9.1/1.9.2 SCADA GI correction still had one wrong UI behaviour: when GI received negative confirmation, Value Viewer placeholders were immediately marked as `GI rejected / wait Class 2`.

That was misleading. A negative confirmation to C_IC_NA_1 means the GI command was rejected; it does **not** prove that every monitor IOA is invalid or unavailable. Values may still arrive through normal Class 2/background polling.

## Changed

- GI negative confirmation no longer marks all missing IOAs as rejected.
- Missing profile rows stay neutral:
  `waiting Class 2/background scan`
- COT/source text becomes:
  `GI negative; Class 2 fallback`
- The final warning is delayed until the Class 2/background collection window finishes.
- If all expected IOAs arrive during the Class 2 window, GI/Class2 completeness becomes PASS.

## Behaviour

- GI negative confirmation:
  - no forced Class 1 drain
  - no mass C_RD_NA_1 read recovery
  - continue Class 2/background scan
  - keep placeholders neutral
- Only after the window expires are remaining placeholders marked:
  `not returned by GI/Class 2 window`
