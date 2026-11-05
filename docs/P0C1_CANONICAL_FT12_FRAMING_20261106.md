# P0-C1 — Canonical FT1.2 stream framing, source-safe Master adapter

**Checkpoint:** 2026-11-06. **Requirement:** P0-C, canonical framing section of the project engineering directive; partial P0-C milestone. This is not a full IEC101/103/104 conformance certificate.

## Audit: root cause and scope

The P0-A core introduced a canonical `Ft12WireLayout` for complete telegram validation, but asynchronous `Master/Protocol/Ft12StreamReader` independently read `0x10`/ `0x68` frame boundaries while `Monitor/PassiveFt12Scanner` performed separate buffered incremental boundary recognition. The two paths could disagree on duplicated length bytes, corrupt stream fragments, wrong end markers and resynchronization. Parser consistency alone does not guarantee byte-stream framing parity.

### Implementation

- `Core/Parsing/Ft12IncrementalFramer` now owns the **single incremental FT1.2 buffer/boundary/resynchronization algorithm** for 101 and 103, with a fixed 262-byte pending array and bounded 128-byte noise batches. Whole-frame checksum and structure are verified by the existing `Ft12WireLayout` (not by a second independent checksum formula).
- `Monitor/PassiveFt12Scanner` retains its public API but delegates all framing to Core, preserving original source offsets, raw bytes, noise/partial candidates and the previous two-consistent-fixed-frame confirmation rule for alternate link-address widths. The raw archive remains authoritative.
- `Master/Protocol/Ft12StreamReader` adapts the same Core engine to the existing `IByteTransport`, keeps the existing `ReadFrameAsync` API, and never adopts alternate link widths. Master retains complete bad-checksum candidates and damaged-end frames **only where the rest of the envelope/checksum remains verified**; doubly-corrupt candidates resynchronize one byte at a time rather than consuming a possible next valid start. Existing response validators still own FCB/FCV/frame-quality decisions; malformed headers/noise are not upgraded to a valid application response. A lost/corrupt FT1.2 prefix makes a resynchronized `0xE5` ambiguous: it is conservatively suppressed, counted at the reader and cannot silently advance an active link ACK. Benign unrelated noise can still precede a legitimate E5.
- Per-link/framer state is independent. Timeout discards an incomplete candidate on persistent reader reuse, while external cancellation propagates. No TX, GI, request/poll timer, transport configuration or link state machine is changed.

This is a deliberate transport/validation boundary. Application codec differences remain unchanged: IEC101/104 share application semantics and IEC103 continues to use its separate FUN/INF interpretation. An orphaned one-character response after byte-level corruption cannot be proven physically causal without external timing/transport evidence.

## Deterministic acceptance

- Core: 0/1/2 link widths, split at every byte, fixed/variable/E5/A2, checksum failure, invalid header resync, EOF partial, adaptive width requiring two validated frames, strict width mode, malformed end and **50,000 valid frames** in one bounded soak with measured time/thread allocations.
- Monitor: exact emitted byte-offset/raw/checksum/partial/noise parity between public wrapper and Core across irregular chunks.
- Master: existing 101/103 consumers plus new scripted transport tests: 0/1/2 widths, two sequential frames, bad checksum/end, damaged header followed by valid response, stale partial across timeout, externally requested cancellation, no incidental TX and damaged-header embedded E5 cannot be mistaken for a reply.
- Exact PR head must pass Windows Release, protocol smoke, xUnit, real WPF OBS-C acceptance, CodeQL, dependency review and hygiene before merge. Monitor's existing SICAM field-shaped fixtures and P0-B 2,560-object durable evidence must remain green.

## Deferred P0-C gaps

The active Master still uses one byte at a time from its transport: this milestone prioritizes safe framing parity, *not* a claim of bulk-read throughput improvement. Meaningful 1200-bps timing and 2/4 isolated COM physical FAT, full IEC104 APCI fragmentation/sequence stress, IEC103 disturbance conformance, decoder width/profile ambiguity, 50k **mixed** process event observability, and whole-app memory/latency baselines remain separate verification items. A synthetic 50k framer test cannot prove zero USB/UART/RTU FIFO loss.

The full raw archive remains independent and optional; Monitor's historical session cache is not the raw journal. No hidden Class 1 polling, GI or link reset changes may be made to mask missing SOE.
