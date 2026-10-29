# R1 — Observed IEC-101 Master to RTU Control Trace Forensics

**Date:** 2026-10-29. **Requirement:** CMD-01 + TRACE-01. This is a first incremental evidence slice, **not** full CMD-01/CAPTURE-01 completion or field certification.

## Operator workflow

1. Configure the existing **IEC-101 Passive Monitor** with two receive-only COM inputs (SCADA TX, RTU TX), or four receive-only inputs for redundant A/B. Set Link/COT/CA/IOA widths correctly.
2. Press **Start Monitor** with recording OFF for fast live interpretation. For full, replayable evidence, explicitly enable **Record entire raw session to disk** BEFORE Start Monitor.
3. Reproduce the control from SCADA. In the existing **Frame Trace** inspect **Master SELECT**, **RTU ACTCON positive/negative**, **Master EXECUTE**, **RTU ACTTERM / DEACTCON / protocol error**. Physical frames retain original raw HEX and typed fields; no separate window.
4. A COT7/P-N=1 reply is a **negative activation confirmation**, not a serial link NACK. The trace includes CA, IOA, Type, S/E, circuit and original request offset when uniquely seen. COT7 alone cannot identify the device-specific interlock, authorization or local/remote reason.
5. COT44/45/46/47 are named **application protocol errors**: inspect Type/COT/common address/IOA and the selected interoperability list. Missing request, bad checksum, unexpected phase, overlapping request and incomplete capture are **unverified/ambiguous**, not RTU rejection.
6. Select relevant frames using Ctrl/Shift and **right-click Copy Selected Frame**, or save selected evidence as a reopenable .ariec package. Do not share private field captures without authorization.

## Engineer-facing examples (synthetic, not field measurements)

| Observed traffic | Defensible meaning |
|---|---|
| Master Type46 COT6, SELECT, CA5 IOA123 | Control SELECT requested; no RTU acceptance yet. |
| RTU Type46 COT7 P/N=1 | RTU negatively confirmed activation; inspect command permissions, interlocks, local/remote state, qualifier and mapping. Internal root cause not known from COT7 alone. |
| RTU Type46 COT7 P/N=0 | Positive application ACTCON; not physical movement. |
| Master Type46 EXECUTE and RTU ACTCON/ACTTERM | Protocol-phase acknowledgment/termination; correct process feedback and actual position still need independent evidence. |
| RTU Type46 COT46 | Unknown common address reported; check CA/interoperability profile. |
| ACTTERM without matching ACTCON in visible evidence | Incomplete transaction history; no proven success. |
| RTU response on B while request seen only on A | Different physical source; do not cross-correlate. |
| E5 or link ACK alone | Data-link proof only; not command acceptance. |

## Engineering architecture and limitations

- Iec10xObservedControlTimeline in Master/Protocol/Iec10x consumes **existing typed ASDU** and original receive offsets, without a second parser or raw-string re-decoder. Pending state is bounded to 128 source+CA+IOA+Type+originator keys.
- Existing Passive Monitor FeedPassiveFrameToExistingViews feeds the typed timeline into the same EvidenceRow and Iec101TraceNarrator. Exactly one physical Trace row, with a compact **Control sequence** line and a concise warning on negative/application errors in existing Diagnostics.
- **Live preview is bounded.** Displayed frames may omit observations under overload or after retention, even if a separate full journal was enabled; this first-slice timeline **does not** claim durable complete command-stage indexing. Reopen a verified full journal to reanalyze a complete source. Selected excerpts remain explicitly partial.
- No implicit transmission, false missing-response timeout, inferred physical switch action, cross-circuit pairing, or nationwide hardcoded profiles. Missing/bad quality and actual feedback target mapping are not synthesized.
- **Still open:** independent durable command index before GUI dropping queue, target-state feedback per user map, IEC103/104 selected-byte Inspector parity and real 2/4 COM FAT.

## Acceptance before merge

- Typed unit regressions: positive/negative COT7, COT44-47, SELECT/EXECUTE, ACTTERM missing ACTCON, source isolation, echo phase mismatch, overlapping commands, malformed/TEST and noncommands.
- Desktop unit regression: valid actual FT1.2 Type46 negative COT with raw HEX and human-readable Trace note.
- WPF passive-gui-smoke: original Master SELECT → RTU negative ACTCON appears directly in existing Frame Trace without opening COM.
- Windows Release, full xUnit + protocol smoke, CodeQL and ZIP package on exact head; hardware tests are a separate gate.
