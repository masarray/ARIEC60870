# Phase-C3A — Source-owned IEC-101 process integrity and explicit loss accounting (2026-11-06)

**Scope:** Incremental Phase-C3 engineering slice on top of merged #91–#94. **Not** a full Phase-C3, IEC101/103/104 conformance certificate, or physical field FAT. Primary PRD requirements: EVENT-02 / PERF-01; architecture: ADR-05/06. The canonical FT1.2 and Iec10x ASDU codecs stay unchanged.

## Structural weaknesses

1. The passive writer counted accepted/indexed facts and GUI frame-preview loss, but did not publish separate trustworthy counts for secondary FT1.2 frames, malformed frames, decoded/rejected ASDUs or eligible/noneligible individual objects. Thus a count discrepancy could not be reconciled at its originating layer.
2. GI/cyclic digital state tracking previously used a value supplied with invalid/blocked/substituted/not-topical quality, or with TEST/negative COT flags, as a new baseline. A later valid value could then be falsely presented as a process transition.
3. Temporary process-event history returned raw bytes with `ChecksumValid=true` even if the indexed record was accidentally corrupted on disk after capture. Final GUI preparation had another FT1.2 check, but the writer-owned fact API itself lacked an independent stored-record integrity check.

## Implementation contract

- One existing writer-classifier owns `PassiveProcessDecodeCounters` with separate observed nonvariable, invalid variable, primary and secondary frames, decoded/rejected ASDUs, decoded/accepted/excluded individual objects, and untracked baseline capacity. At session completion assert frame, ASDU, object and accepted/indexed/unindexed accounting invariants. No inferred event from bytes not observed.
- Only credible quality and non-TEST/non-NEG digital observations update the last-known state; suspicious observations remain raw/process evidence where the COT policy accepts them. State tracking does not become a second event source.
- Temporary append-only process facts store CRC32 for header and full source-frame payload, using the journal's canonical CRC32. Reads fail closed with `InvalidDataException` on corruption; CRC32 detects accidental corruption, is **not** authenticated storage. Existing P0B file format was temporary per-session, not public persistent archive. Full-session opt-in raw archive and its SHA/verification remain separate.
- Manifest adds `ProcessDecodeAccounting`; live progress uses exactly the writer-side counters. GUI labels the total **OBS** (process observations), not a claim that every received object was a physical SOE. Rejected FT1.2 and ASDUs are made visible.
- No extra polling, TX, GI, master state-machine change, buffer enlargement, second parser or WPF-only patch.

## Deterministic verification

- Valid 4-object IEC-101 timestamped DP frames, invalid quality, blocked quality, TEST/negative GI, independent circuit A/B and later credible state change.
- Accidental temp-index raw corruption must fail closed and must not fabricate a fact.
- 50,000 decoded objects with mixed spontaneous/GI/background COT, two physical RX circuits, explicitly corrupted checksum and master PRM frames. Require exact object/ASDU/frame counter equalities; timing/allocations are emitted by the CI machine and cannot establish field performance.
- 5,000 live-only objects over four fake RX taps with writer-owned real framing/ASDU/index and preview rollover; accepted/indexed/unindexed balance and source identity must survive. Existing 2,560-object raw-recording and decoder/GUI regression tests remain required.
- This slice does **not** prove IEC103/104 end-to-end ledger parity, 50k entire WPF pipeline, cross-mode replay/report equivalence, user-visible p95 <=150ms, or hardware 1200bps FIFO continuity. These remain explicit Phase-C3/C4 gates. Never equate successful CI with SCADA receipt of ~2,000 field events.

## Evidence status

Implementation is staged until exact-head Windows Release, full xUnit, protocol smoke, WPF, CodeQL, dependency review and hygiene pass. Record measured CI results and any failed gates on the PR before merge. Physical FAT is unverified.
