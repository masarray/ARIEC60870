# Phase C3-B incremental — IEC-104 trusted receive admission (2026-11-06)

**Dependency:** Draft PR #95 (Phase-C3A) on master `0967a590ed1f07f103195c0a4a7233adfb934e4d`. Scope is a production admission correction, not completed Phase-C3 or physical FAT.

## Root cause / regression

In `Iec104ClientSession.ReceiveOneAsync`, an out-of-window N(R) produced `IEC104-NR-ACK-INVALID` but did not block `acceptedI`: the same I-frame could still advance N(S), increment user-data/GI counters and publish process facts. A supervisory S-frame with an out-of-window N(R) was also counted as a valid acknowledgement. `DrainBurstAsync` could terminate on a decoded but **rejected** COT10. In Desktop, `IsIec10xProcessValue` provided a fallback based on Type/IOA even when the engine marked a received I-frame as `RX Warning`. Thus rejected evidence could still mutate Values.

## Owning correction

- IEC-104 Master makes **one** receipt decision including APCI syntax, STARTDT state, exact N(S) and N(R) inside the outstanding TX window. Invalid N(R) never advances the window or the RX sequence; raw frame and finding remain. Rejected I/S frames are explicitly counted with `RejectedFrames`.
- The receive result carries an `AcceptedI` verdict from the same authority to handshake/burst processing: only accepted I-format COT10 can end a burst as GI evidence. No second sequence validator, reader or event ledger.
- The existing Desktop frame Trace still displays rejected RX, but the event/value projection rejects typed IEC-101/104 RX whose canonical decoded payload failed the existing trusted-evidence predicate. This prevents Type/IOA-only fallback from promoting engine-rejected data. WPF does **not** independently interpret N(R).
- Deterministic test sends a valid S-frame with future ACK, a valid I-frame with future ACK and SQ0 two-object spontaneous data, followed by a valid retransmission with the *same* N(S). It checks rejected counts, no false ACK, no rejected-object promotion, exact two accepted process objects and common physical frame identity. Real network/serial timing is not simulated as FAT.

## Non-goals / remaining gates

This slice does not prove full IEC-103 protection event storage, all IEC-101/103/104 replay/report parity, IEC-104 reconnect across real TCP sockets, full 2k/5k/50k mixed production-pipeline benchmarks, actual WPF p95, bounded retention under long running active sessions, or physical 1200-bps delivery of ~2k RTU SOEs. These remain **unverified** and PR stays Draft. Exact-head CI, CodeQL, dependency and hygiene outcomes must be checked after this commit, not extrapolated from older results.
