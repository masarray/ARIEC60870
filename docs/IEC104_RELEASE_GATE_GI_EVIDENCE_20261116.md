# IEC-104 release-gate: source-qualified GI and bounded evidence

Date: 2026-11-16. Work based on `master@2aaf2d8f9438f3535e414c93ec03029983af9564`.
Scope: `CMD-01`, `EVENT-01`, `PERF-01` and issue #120 P0 GI false-completion audit.

## Root cause confirmed in runtime source

`Iec104ClientSession.ReceiveOneAsync` counted any accepted I-format
`TI100/COT10` as a GI end, without checking a sent intent or a prior
positive ACTCON for the same CA/OA/QOI. `DrainBurstAsync` stopped on
the same unmatched on-wire shape; this could truncate the startup/manual
GI follow-up window when a misleading but well-formed message arrived.
The existing `Iec10xGiTermination.IsObserved` correctly describes this
helper as a **structural observation only**, not transaction proof.

IEC-104 Master also retained all session evidence in an unbounded
`List<Iec103MasterEvidenceEvent>`. IEC-101 single and A/B already use a
bounded FIFO, and changing a separate UI list would not repair this
Master-report allocation.

## Integrated solution

- Reuse one authoritative per-association GI tracker already tested for
  IEC-101. Move its implementation to protocol-neutral
  `Iec10xGiLifecycleTracker`, keeping the IEC-101 public compatibility
  class and existing tests. No second GI parser or state machine.
- Reset authority at each newly opened TCP connection. Admit new GI
  intent only when an actual I-format transport write completes.
  Inspect RX only after APCI STARTDT/N(S)/N(R) acceptance.
- Require matching TI100/COT7 positive ACTCON, then matching TI100/
  COT10 positive ACTTERM with same CA, OA, QOI, type and fresh connection.
  Negative, TEST, malformed, unmatched and prematurely terminated
  conversations are evidence, **not success**.
- Only a matched ACTTERM increments `GiEndResponses` and shortens
  a GI burst window. COT10 for a command or another station remains
  independent and never short-circuits receive processing.
- Share the IEC-101 O(1) evidence FIFO with IEC-104 through a generic
  `Iec10xBoundedEvidenceHistory`. Count exactly
  `EvidenceEventsDroppedFromMemory`; callbacks still see every event.
  No silent changes to TCP/APCI, commands, CLI, WPF or RTU parameters.

## Acceptance / limitations

Deterministic scripted I/S/U peer tests: correct ACTCON+ACTTERM,
wrong station, wrong QOI, wrong originator, no ACTCON, negative ACTCON
or ACTTERM, TEST-bit ACTCON or ACTTERM, unrelated command COT10,
multiple interleaved valid-I ASDUs. Evidence FIFO synthetic batches:
60, 500 and 2000 events with exact eviction accounting.

A matched GI lifecycle does **not** certify expected IOA coverage,
positive breaker feedback, peer conformant implementation or multi-vendor
FAT. The native Windows installer upgrade and 1200-bps physical SICAM
FAT remain separate release evidence. See issue #120 and #162.
