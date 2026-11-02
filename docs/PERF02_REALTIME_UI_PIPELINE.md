# PERF-02 — Bounded passive preparation and coalesced WPF projection

**Scope:** IEC-101 live RX-only preview and shared active/passive desktop UI; implementer's checkpoint. Requirement authority: PERF-01 in IEC10X_INTEROPERABILITY_PRD_20261029.

## Implemented
- Passive raw journal and protocol/session transaction authorities remain unchanged. Live preview dequeues at most 180 original frames per 160-ms poll; each nonempty frame batch uses a **single** background preparation task that extracts ASDU bytes and decodes by the existing canonical IEC10x decoder, preserving source-feed ordering. No per-frame task and no second decoder implementation. WPF's existing sequential projection and all-object processing still own Values/Event/Trace presentation.
- One 75-ms WPF flush coordinator for both active and passive; passive 160-ms timer handles byte/preview and transaction feeds only. `HOLD` and raw-chunk `Freeze` do not control RX or evidence journal writes.
- Counter labels are committed once per UI batch rather than four TextBlock writes for each physical frame. QuickFind counts rely on coalesced collection notifications instead of a redundant every-75-ms scan. Empty status-label writes are elided.
- CA/IOA/type-family profile resolver uses an allocation-free single scan; identical semantics for exact type, timed/untimed family, ambiguous and wrong-CA entries.

## Proof gates
- Mixed 800-frame deterministic type-1 SQ0 multi-object preparation retains order, bytes and both objects. Invalid/truncated frames preserve raw and do not invent decoded points.
- Existing protocol smoke, 5k/50k desktop/event stress, xUnit, live WPF acceptance, Release warnings and format must pass on exact PR SHA; build artifacts are synthetic GUI proof only.
- Physical 2/4-COM capture, noisy parity/CRC, Win11 UI input latency, CPU/p95 Dispatcher time, GC allocation and real hardware FAT remain **not measured** by GitHub CI. Do not describe this change as an achieved FPS, throughput or certified losslessness.
- Per-frame work still remaining on WPF (control/transaction classification, Values/Event row creation) is an explicit future optimization target and must be benchmarked before another worker boundary is added.

## Operational safety
Passive capture's existing `TryReadFrame` preview channel is bounded and may drop old UI preview when overwhelmed; permanent verified raw recording is independent and only exists when explicitly enabled. Do not equate this bounded preview with complete journal coverage or claim zero loss where recording is off.
