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

## IEC-101 dual-link SQ0/SQ1 original-frame parity

The dual-link channel previously emitted one event for the first information object and additional events for siblings, but the session-owned sequence assigner did **not** link the sibling events to the originating physical frame. Individual event sequence numbers were incorrectly used as separate original-frame identities. The channel now obtains the session-owned frame sequence from its existing publish callback; siblings carry `PhysicalFrameSequenceNumber`, and the session preserves it when enriching evidence. No new event source or physical identifier is invented. The existing IEC-101 sequence-GI simulation asserts that object 100 and object 101 have distinct event sequences but exactly one original frame root and physical Link A identity.
