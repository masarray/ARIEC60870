# M1: IEC-101/104 Command and Response Forensic Semantics

Related: [Issue #39](https://github.com/masarray/ARIEC60870/issues/39)

## Field requirement
Command actions and the RTU's answer must be decipherable with the same rigor as a spontaneous event: COT numeric/flags, raw qualifier and its bits, Type, CA, IOA, current link/APCI context, source bytes and exact ASDU information-element offsets. Never report successful switchgear motion merely because a link ACK or COT7 ACTCON was observed.

## M1 implementation (this branch)
- Add immutable semantic facts `Iec10xCauseDetails`, `Iec10xQualityDetails`, and `Iec10xCommandDetails` owned by the IEC-10x decoder layer (not re-parsed from UI strings).
- Preserve numeric raw COT and flags P/N, TEST, OA (including zero when second COT octet configured). Explicit phase/outcome descriptions for COT5/6/7/8/9/10/11/12/44..47.
- Decode per-information-object quality (SIQ, DIQ, QDS and BCR); overflow only exists for QDS. BCR carry is not overflow. All inactive quality bits and N/A remain available in structured properties.
- Preserve ASDU-relative information-object and element offsets. For SQ1 subsequent objects, the implicit IOA has no physical on-wire IOA offset (represented by -1).
- Type-aware command qualifier interpretation: single/double/regulating, select vs execute, QOS/QU, QOI, QCC, QRP, read/clock/test baseline. Decoder never infers successful primary-process actuation from protocol confirmation.
- Carry command/COT/quality information as typed evidence through IEC-101 Active Master, IEC-104 client, and IEC-101 Passive Monitor, and include all details in selected Frame Inspector.
- Fix compact HEX from Passive Monitor being treated as a single byte in Frame Inspector; fix IEC-101 FC9 NO DATA count by numeric function code.

## M1 deliberate boundaries
This is the **first implementation milestone**, not the finished unified forensic system.

Not yet implemented here:
- Multi-stage command transaction correlation (SBO SELECT → ACTCON → EXECUTE → ACTCON → ACTTERM and optional returned state), with ambiguity, missing/timeout and RTU processing timing caveats. Existing passive request/reply correlator remains in place.
- Structured full byte-range map and interactive bit overlays for every field, especially 0/2-byte link addressing; M1 exposes exact ASDU qualifier element offsets but the old UI grouping still has known fixed-offset limitations.
- Compact premium badge templates in the single Quality column on Values/Events; typed flags now exist as the source for that follow-up. **Do not add per-flag columns.**
- Event Log coverage correction for all first-observed/repeated spontaneous event objects; complete per-object provenance and new capture schema.
- Protocol-specific IEC-103 command data model; current milestone is IEC-101/104 shared ASDU.
- UI thread/performance baselines, Windows manual GUI, full hardware SICAM interoperability. Require CI and field verification later.

## Next milestones
M2: immutable canonical Frame/ASDU/Object/Transaction IDs, Active Master + Passive + Replay parity, event provenance and action lifecycle.
M3: one Quality badge cell, one COT cell, linked full-bit/field inspector + source-highlight, report/.ariec parity.
M4: Windows GUI smoke, sustained replay/CPU/memory benchmarks and reproducible hardware test build.

## Public synthetic tests (no private captures)
Test C_SC SELECT, ACTCON positive, C_DC EXECUTE negative P/N, TEST and COT47 error, ACTTERM and QOI, DIQ overflow N/A, QDS OVF+IV, BCR carry, SQ1 exact byte offsets. Tests are in `tests/ARIEC60870.Master.Tests/Iec10xCommandForensicsTests.cs`.

## Engineering guardrails
Do not alter scheduling/FCB state-machine behavior in M1, and never add TX code to Passive Monitor. Parser work must be pure, bounded and independent of WPF. Never infer actual switchgear movement without later process-state evidence. Raw capture evidence is private and must not be committed to public fixtures.

## M1 continuation — typed per-object source and timestamp correctness

- Retain a shared decoded ASDU and selected information-object reference in every IEC-101/104 Active Master, IEC-101 redundant and IEC-101 Passive event. Reuse one source record rather than parsing from WPF strings or copying decoded raw bytes per object.
- Secondary objects preserve their own typed COT, command, quality and exact ASDU-relative object/element byte offsets, even for SQ1 implicit IOAs. The inspector shows the selected object's metadata and a bounded first-page overview of an ASDU's objects.
- IEC-101 E5 in the inspector is contextual (link ACK versus Class 1/2 no-data), never proof of successful application command.
- CP56Time2a day-of-week high bits must not be treated as invalid-time. Clock/calendar bounds and minute IV are checked. CP24 hourless clock ranges are checked. Public regression fixtures cover valid DOW and invalid ranges.
- Passive multi-object projection encodes HEX once per frame instead of once per IOA. Full command transaction pairing, completed premium badge templates and report/capture SSOT remain M2/M3; no field-device FAT is implied.
