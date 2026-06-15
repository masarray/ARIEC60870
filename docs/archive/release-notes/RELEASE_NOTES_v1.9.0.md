# ARIEC60870 v1.9.0 — Smart IEC-101 GI Recovery

## Fixed / Improved

### IEC-101 GI Smart Recovery

GI completeness is no longer judged only by ACTTERM. When ACTTERM is observed but expected profile points are still missing:

1. The app queues targeted `C_RD_NA_1` read commands for missing monitor IOAs.
2. The Value Viewer updates as read responses arrive.
3. Final GI verdict is delayed until the recovery window closes.
4. Remaining missing points after read recovery are reported as real RTU/profile completeness findings.

### Manual GI

Manual GI from the command dock now resets the GI completeness watch and then performs the same post-GI proof flow.

### IEC-101 Engine

The post-GI Class 2 verification sweep is now adaptive:
- no longer limited to only 3 polls,
- runs up to a bounded profile-aware sweep count,
- stops after consecutive NO DATA once user data has been observed.

## Why

Many IEC-101 outstations do not put all monitor points into the Class 1 GI queue. Some values appear only in Class 2 background polling, and some require direct read. The app now treats GI as a proof workflow instead of a single ACTTERM event.
