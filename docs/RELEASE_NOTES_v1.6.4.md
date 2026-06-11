# v1.6.4 — GI Drain + Spontaneous Digital Event Proof Pass

This release fixes two field-critical regressions found during IEC-101/104 forensic validation.

## Fixed

- IEC-101 GI follow-up drain no longer stops only because ACD becomes 0 after the first user-data response.
- GI drain now continues until ACTTERM, NO DATA, cancellation, or the configured Max Class 1 drain limit.
- SP/DP process objects are now forced into Value Viewer even if an upstream evidence flag is missing.
- SP/DP spontaneous objects with COT 3, 11, or 12 are now forced into Event Log.
- IEC-101/104 time-tagged digital objects Type ID 2/4/30/31 are included in value/event classification.
- IEC-101/104 demo simulation now injects spontaneous SP/DP events so the event log can be verified without hardware.
- Type ID 33 M_BO_TB_1 CP56Time2a length/timestamp handling was completed.

## Forensic behavior change

IEC-101 GI is now treated as a station interrogation transaction, not as a normal ACD-driven event drain. This matters because some outstations clear ACD between GI response frames even though more interrogation data is still available. The analyzer now records a finding if the GI drain reaches the configured limit before ACTTERM/NO DATA.

## Validation

Sandbox validation performed:

- XAML XML parse: OK
- C# brace balance: OK
- ZIP integrity: OK

Full `dotnet build` must still be run on a Windows/.NET SDK machine.
