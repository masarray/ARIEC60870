# ARIEC60870 v1.9.4 — Scan-Tolerant GI Regression Fix

## Fixed

The previous GI patches were still too verdict-driven. They allowed GI state to overwrite Value Viewer placeholders with rejected/not-returned labels. This made the UI look like the RTU failed, even while live Class 2/background scan could still deliver values.

## Changed

- GI no longer destructively updates Value Viewer placeholders.
- Seeded IOA rows remain neutral until actual Class 1/Class 2 frames update them.
- GI negative confirmation is recorded as protocol evidence, not used to reject all profile IOAs.
- Startup/manual GI now always performs bounded Class 1 drain again, then adaptive Class 2/background sweep.
- Class 1 empty / NO DATA is treated as normal scan condition, not as missing-value verdict.
- Final observation note is non-destructive:
  `IEC101-SCAN-PROFILE-PENDING`
  instead of row-level `not returned by GI/Class 2`.

## Behaviour

Actual received IOA frames are the only source allowed to overwrite Value Viewer values.
GI / Class 1 / Class 2 status is diagnostic evidence, not destructive row state.
