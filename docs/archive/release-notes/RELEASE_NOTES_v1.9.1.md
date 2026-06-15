# ARIEC60870 v1.9.1 — SCADA-Style IEC-101 GI Behaviour

## Corrected

The previous GI recovery approach was too aggressive. It could force Class 1 drain / mass read recovery even when the outstation had already negatively confirmed GI or when Class 1 was empty. This release changes GI behaviour to match practical IEC-101 SCADA master workflow.

## New IEC-101 GI State Policy

### GI negative confirmation

If the outstation negatively confirms C_IC_NA_1:

- Do not force Class 1 drain.
- Do not mass queue C_RD_NA_1 read requests.
- Mark Value Viewer placeholders as `GI rejected / wait Class 2`.
- Continue normal Class 2/background polling.
- Raise `IEC101-GI-NEGATIVE-CONFIRMATION`.

### GI accepted but Class 1 empty

If GI is accepted but ACD/Class 1 is not pending:

- Do not treat Class 1 empty as failure.
- Open a bounded Class 2/background collection window.
- Values arriving through Class 2 are valid proof and update Value Viewer.
- Final completeness verdict is delayed until the collection window ends.

### GI accepted and ACD=1

If GI is accepted and ACD indicates pending Class 1 data:

- Drain Class 1 in a bounded loop.
- Then continue adaptive Class 2/background sweep.
- Final completeness is based on profile points received from both paths.

## Removed

- Automatic mass C_RD_NA_1 read recovery for missing GI points.
- Early GI-incomplete warning immediately at ACTTERM.

## Why

Many IEC-101 outstations provide monitor/measurement values through Class 2/background scan. Class 1 is for high-priority/event data and should not be blindly forced when empty.
