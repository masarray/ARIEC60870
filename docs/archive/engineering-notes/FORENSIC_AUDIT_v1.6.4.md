# Forensic Audit v1.6.4

## Field issue reproduced conceptually

Observed symptom:

1. IEC-101 General Interrogation did not drain all data.
2. SP/DP objects were visible in raw/trace evidence but did not reliably appear in Value Viewer or Event Log.

## Root cause

The IEC-101 Class 1 drain logic used one stop rule for two different situations:

- normal spontaneous event drain after ACD=1
- General Interrogation follow-up drain after C_IC_NA_1

The old rule stopped after a user-data response when ACD became 0. That is acceptable for a normal event drain, but it is unsafe for GI because many outstations return GI data across multiple Class 1 responses and may not keep ACD asserted until the final ACTTERM.

## Correction

GI follow-up drain now uses a dedicated stop policy:

- stop on ACTTERM
- stop on NO DATA
- stop on cancellation / duration end
- stop on Max Class 1 drain limit with a finding

It does **not** stop merely because ACD clears after one user-data response.

## UX correction

Value Viewer and Event Log no longer depend only on `IsRelayValue` / `IsRelayEdgeEvent` being set by the protocol session. The UI now has a defensive classifier:

- IEC-101/104 process Type IDs 1..16 and 30..37 are shown in Value Viewer when IOA exists.
- IEC-101/104 digital Type IDs 1/2/3/4/30/31 with COT 3/11/12 are shown in Event Log.

This makes the UI more forensic-proof: decoded process data cannot disappear just because a convenience flag was not propagated.

## Remaining gaps

- IEC-104 k/w/t1/t2 enforcement still needs full state-machine validator.
- IOA profile import and GI completeness matrix are still required for final FAT-grade proof.
- Command Behaviour Validation Studio is still required for direct/select-execute validation.
- Immutable evidence package still needs raw stream + hash manifest.
