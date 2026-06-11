# v1.6.5 — GI/C1/C2 Engine Activity Indicator + Deep Standards Audit Pass

This release improves operator situational awareness and documents the next protocol-forensic gaps for IEC 60870-5-101, IEC 60870-5-103 and IEC 60870-5-104.

## UX improvement

### GI/C1/C2 live indicator

The header card previously showed only `C1/C2` activity for serial protocols, or `I/S` activity for IEC-104. This made it hard to see when the engine was still inside a General Interrogation workflow.

The activity card now shows:

- `GI/C1/C2` for IEC-101 and IEC-103.
- `GI/I/S` for IEC-104.

The green GI lamp pulses whenever evidence indicates General Interrogation activity, including explicit GI commands, GI follow-up drain, interrogation COT 20..36, activation/termination messages, or other GI-related protocol evidence. The numeric activity counter is now ordered as `GI / C1 / C2` for serial protocols and `GI / I / S` for IEC-104.

## Forensic report wording fix

The exported master evidence report no longer says GI follow-up drain stops on ACD clear. Normal Class 1 event drain may stop when ACD clears, but GI drain is stricter and continues until ACTTERM/GI END, NO DATA, cancellation, DFC busy, or configured drain limit.

## Deep audit result

See:

- `docs/FORENSIC_AUDIT_v1.6.5.md`

Main remaining gaps:

1. IEC-104 full state-machine validator: t1/t2/t3/k/w enforcement, pending I-frame ledger, delayed S-frame ACK, unexpected N(R), duplicate/stale sequence and STOPDT behaviour.
2. IOA point profile import and GI completeness matrix.
3. Command Behaviour Validation Studio for direct operate, select-before-operate, ACTCON, ACTTERM, negative confirmation, wrong CA and unknown IOA.
4. IEC-103 deeper relay forensic: generic services, private ranges, disturbance/file-transfer workflow and vendor interoperability templates.
5. Immutable evidence package with raw stream, frame hash, session hash, profile snapshot and manifest.

## Validation performed in sandbox

- XAML XML parse: OK
- C# brace balance: OK
- ZIP integrity: OK

Full `dotnet build` still needs to be run on a machine with the .NET SDK installed.
