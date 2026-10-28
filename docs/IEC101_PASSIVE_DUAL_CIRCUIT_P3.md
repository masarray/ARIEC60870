# ARIEC60870 IEC-101 P3 — Four-RX Redundant Serial Passive Capture

Date: 2026-10-28. Baseline: P0–P2E merged on `master` at `d58892ef1fe9763c02ec063724164f4b9d8c9eb2`.

## Operator goal

Analyze *both* physically separate redundant IEC-101 circuits concurrently without touching the production master/slave data or opening transmit-capable paths:

- **Link A:** RX0 listens to A SCADA TX; RX1 listens to A RTU TX.
- **Link B:** RX2 listens to B SCADA TX; RX3 listens to B RTU TX.

Each RX requires a separate electrically approved high-impedance/isolated tap. No monitor port TXD/RTS/DTR must touch production. Do not infer failover from the absence of frames on one line; NUC state and external IEC104 records are separate evidence.

## Existing GUI

Use the existing Protocol Mode dropdown -> IEC-101 Passive Monitor. In the serial setup, the existing Link A COM pair remains available. Tick **Also monitor redundant Link B (4 RX ports)** to expose two extra Link B COM choices in the same panel. This is **not** a new window, tab or button. Every COM must be distinct and both directions of each circuit must use compatible UART framing.

Default remains **2 RX** for old setups and archives. Four COM inputs can be used only when all four ports are configured. Saving/loading Setup must preserve the checkbox and both Link B port names. Frame Trace, Value Viewer, Event Log, raw preview and Capture label each frame with Link A/B and retain original bytes; IOA keys include circuit identity to prevent Link A and B overwriting one another.

## Disk-first architecture

The original 40-byte `APM1` record format and CRC32 remain unchanged. Each input writes its own `stream-00.bin` through `stream-03.bin`; `manifest.json` contains either 2 or 4 uniquely identified streams with COM port, physical transmitter, UART settings, record count, raw bytes, driver errors and SHA256.

One shared disk-backed writer consumes from four bounded RX queues, each with an independent dedicated read worker and its own monotonic byte offset/sequence. Writer has one FT1.2 scanner **per physical direction**, and independent request/response/GI matching by physical pair (stream 0↔1, 2↔3). Addressless E5 must never cross circuits even if both use the same IEC-101 Link Address and appear active at once. The already implemented 5-second P2E timeline has independent circuit-bucket state and a `CircuitId` field. Derived evidence JSONL remains rebuildable from authoritative raw files.

All RX workers are armed before capture and must all stop safely, even on a failed source or disk. A slow WPF preview cannot block the writer; UI notifications are bounded while full raw journals and derived indexes remain disk-backed.

## Replay and backward compatibility

`PassiveJournalVerifier.Verify` accepts finalized sessions with **exactly two or four distinct ordered stream IDs**, verifies **every file** and SHA256 and refuses incomplete/bad captures. `PassiveJournalReplay.ReadVerifiedFrames` performs a deterministic, fixed-four-way timestamp merge while retaining per-source order. It may not claim wire-edge ordering between independent USB adapters. Old 2-source sessions and existing `manifest.json` structure remain supported without migration.

Full offline replay uses selected saved/Setup IEC-101 decoder widths (P2C), the same pure transaction correlator and GI tracker (P2B/P2D), plus separate per-circuit failure timeline (P2E). No serial devices are opened for verified offline replay.

## Acceptance / regression

1. Legacy 2-RX options, recorder and verified replay remain compatible.
2. Reject incomplete Link B pair, duplicated COM name (case-insensitive) or UART mismatches.
3. Four synthetic independent taps receive chunk-split fixed/E5 frames; four correct raw files, offsets, SHA256, final manifest and 4-stream offline reconstruction remain lossless even after deleting derived frame indexes.
4. Opposite-line E5/fixed reply can only match a request on **the same** duplex physical circuit.
5. GI ACTCON/ACTTERM cannot close a request on the other circuit sharing the same CA/OA/QOI.
6. All four COM users can choose existing Setup and return to 2 COM without losing main navigation and tools; WPF GUI smoke.
7. No success claims from two-stream archives about unobserved redundant link. Four COM makes A/B activity observable but **not** the proprietary NUC failover cause.
8. Full Release build, 19 protocol smoke, entire xUnit, WPF smoke, security/hygiene, SHA-verified Windows portable artifact. Physical non-interference test, 2h soak and real SICAM/ETERA comparison remain pending.

## Further phase (not claimed completed)

A paged timeline view with jump-to-frame/byte offset, real NUC role-change event import, configurable per-link profiles when A and B differ, USB timestamp uncertainty modeling and a physical reference-analyzer FAT.
