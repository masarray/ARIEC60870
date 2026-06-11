# Release Notes v1.6.2 — Forensic Timestamp + Link Flag Visibility

This release fixes two field-readability defects found during IEC 60870 forensic audit:

- IED/RTU time-tags decoded from IEC-101/104 ASDUs are now preserved through the event pipeline and shown in Evidence, Value Viewer and Event Log grids.
- Serial frame trace now exposes ACD and DFC as first-class forensic columns for IEC-101 and IEC-103.

## Why this matters

A protocol analyzer must not hide the two pieces of evidence that decide field behaviour:

1. **Device time** — the timestamp carried by the IED/RTU/server inside the ASDU, not only the PC receive time.
2. **Link-layer demand/busy state** — ACD shows pending Class 1 data; DFC shows data-flow/busy condition.

Without these fields, an engineer can see traffic but cannot prove event chronology or polling behaviour.

## Engine changes

- Preserved `RelayTimestampText` and `RelayTimestampInvalid` in IEC-101 `AddEvent()`.
- Preserved `RelayTimestampText` and `RelayTimestampInvalid` in IEC-104 `AddEvent()`.
- Preserved `PreviousSignalValue` and `MappingProfileName` when events are enriched.
- Added FT1.2 single-character NACK `0xA2` recognition in parser and stream reader.
- Counted single-character `0xA2` as NACK instead of generic ACK.
- Added CP24Time2a decoding for legacy IEC-101 time-tagged ASDU types.
- Added BCR/integrated-total quality decode: sequence, carry, adjusted, invalid.

## UX changes

- Frame Trace grid now includes `ACD` and `DFC` columns for IEC-101 and IEC-103.
- IEC-104 hides ACD/DFC because those are FT1.2 link-layer flags, not IEC-104 APCI fields.
- Timestamp columns are renamed to `IED/RTU time` so users do not confuse device event time with PC arrival time.
- Raw frame hint now follows active protocol profile.

## Validation added

- Parser test for single-character NACK `0xA2`.
- Reader resync test for NACK after serial noise.
- IEC-101 CP24 timestamp decode test.
- IEC-101 BCR quality decode test.

## Remaining forensic roadmap

- IEC-104 full t1/t2/t3/k/w state-machine enforcement.
- IEC-101 balanced-mode engine or explicit disabled state.
- IOA profile import and GI completeness matrix.
- Command Behaviour Validation Studio.
- Immutable forensic evidence package.
