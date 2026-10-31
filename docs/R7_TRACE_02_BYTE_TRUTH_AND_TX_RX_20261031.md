# R7 TRACE-02 — Full-direction line monitor and original-byte forensic inspector

Date: 2026-10-31. Implementation notes for the same primary Windows IEC-101/103/104 analyzer. This complements the merged R7 indexed .ariec archive milestone; it does **not** mark all TRACE-02, CAPTURE-02, R8 simulation, or physical FAT gates complete.

## P0 operator observation and root cause

The active IEC-101 demonstration screenshot showed `59 / 116 frames · Focus`: RX link ACK / ACD=1 events were visible, while routine Master TX Class 1/2 requests preceding them were hidden by `ObservationFrameFocus`. This was a **presentation error**, not evidence that a Master TX frame was absent from source. In active IEC-101, hiding TX while showing its RX reply makes an ordinary exchange look unsolicited and undermines the tester's trust. In passive RX-only capture, no application-owned TX is expected; do not invent one.

Corrections:
- **Default Trace = All frames**, not a hidden `Activity focus`; TX, RX and raw chronological order retained.
- Optional Focus **never hides any actual TX**; it can suppress only demonstrably routine secondary no-data while preserving unusual, invalid, AC/DFC and decoded evidence.
- WPF full-frame and Focus invariants test the real viewport. The existing ring, exact source journal, indexed archive, and explicit filter scopes remain; do not manufacture request/response pairings by matching display strings or timestamps.
- Segmented tab selected state uses neutral fill, no blue active border or blue hover stroke; compact density unchanged.

## Source-byte authority for all supported analyzers

1. `Iec10xAsduDecoder` records the actual configured COT/CA/IOA byte widths in each decoded ASDU, including malformed results. Old IEC-101 inspector inferred CA from the first object's IOA offset; that fails for missing/unknown/SQ objects and can yield convincing but false offsets. It now requires valid link checksum/length, matching original ASDU bytes, zero ASDU decoder issues and explicit widths before projecting typed fields.
2. `IecForensicByteInspector` is one **selection-only** entry point for IEC-101/103/104. It returns original physical-frame offsets, exact hex slices and typed explanations; `RebuildProtocolMap` uses it for both map and linked HEX segments. The Copy Decoded command includes byte offsets and lengths and retains original raw frame.
3. IEC-103: actual FT1.2 link boundaries, Type ID/VSQ/COT/CA, FUN and INF byte positions, original information-element range, checksum/end. No IEC-101 IOA or guessed vendor-private multiple-object semantics.
4. IEC-104: envelope and APCI I/S/U, actual N(S)/N(R), typed header and complete decoded SQ=0/SQ=1 objects. SQ=1 later IOAs are **derived semantic addresses with offset=-1/count=0**, not bytes that exist on the wire. A mismatched/invalid ASDU never obtains a fabricated Type/COT/CA/IOA inspector; raw is preserved.
5. Archive: V1 IEC-101/104 written fields use decoder's recorded widths, not derived offsets; explicit `IEC103-FT12-V1` stores its link-width provenance, enabling offline FT1.2 rehydration of new 103 captures. Older 103 captures without that marker remain raw-only. Archive hash verification/legacy warnings continue to be managed by the existing archive reader and **are separate** from link checksum verification.

Performance: no work on RX/COM/TCP from this inspector; no permanent per-object WPF view models, additional parser on hot path or new background worker. Work is bounded to selecting and decoding the current frame, using original typed models and raw bytes.

## Proof and limits

Windows Release build, unit tests for 101 profile widths, 103 FUN/INF archive replay, 104 I/S/U, SQ=1 implied IOA, malformed/legacy controls and WPF screenshot acceptance must pass at PR head. Actual RTU/relay, all-vendor IEC103 information object layouts, optical/RS232 2/4-RX physical isolation, report/PDF round-trip for every multi-object ASDU and 1200-bps endurance remain separate verified milestones. No synthetic CI or simulated IEC-101 screenshot constitutes device FAT.

External independent product research: ASE2000's native line monitor presents **both directions** with raw numeric fields linked to decoded interpretations, and supports Master/RTU simulation separately. This is a functional benchmark, not copied UI or proprietary implementation. See https://www.ase-systems.com/products/ase2000-v2/ and the ASE2000 communication test set user manual.
