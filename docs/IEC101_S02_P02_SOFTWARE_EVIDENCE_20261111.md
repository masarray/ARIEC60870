# IEC-101 M3 S02 + M5/P02 Software Evidence — 2026-11-11

**Development-only checkpoint** for [issue #143](https://github.com/masarray/ARIEC60870/issues/143) and [draft PR #145](https://github.com/masarray/ARIEC60870/pull/145). This is **not** a physical FAT sign-off or an independent-peer certification.

## Why this change was necessary

1. The single-link scheduler already had a `MaxConsecutiveClass1BeforeClass2` fairness exit inside its Class 1 burst, but the outer loop checked `_acd` first and immediately reentered Class 1. An RTU continuously asserting ACD could starve Class 2 indefinitely.
2. Dual-link active scheduling had no equivalent bounded Class 1 fairness exit, so a continuous ACD queue could drain up to the burst maximum before considering other traffic.
3. A timed-out/incomplete FT1.2 prefix could be cleared without recording parser ambiguity. A later unaddressed `E5` could be misread as the next service response when its physical frame boundary was unknown.

## Implementation

- Single-link: a verified, not-busy Class 1 fairness yield actually dispatches one Class 2 poll and updates its scheduling timestamp. GI follow-up maintains its distinct completion semantics.
- Dual A/B: apply the same configurable Class 1 streak limit, permit Class 2 only on the **same still-active healthy owner** after health re-evaluation; never poll the standby; suppress Class 2 under DFC=1.
- Shared FT1.2 reader: expire partial prefix and count `ExpiredPartialFrames` / `ExpiredPartialBytes` on deadline and cancellation; suppress ambiguous E5 until a verified addressed frame boundary is seen; retain genuine E5 handling in unambiguous cases. Cancellation still propagates to Stop.
- Serial close: COM cleanup cannot be skipped merely because the caller's cancellation token was already signaled.

## Reproducible software acceptance

- `Iec101Class2FairnessTests`: synthetic peer asserts ACD on **every** status / Class 1 response; prove 3 Class 1 requests followed by Class 2 on single and active A/B, and no standby Class 1/2/GI/control.
- `Iec101SerialStartupAndTimingTests`: truncated variable/fixed FT1.2 and cancellation followed by E5 are **not** accepted as unique new responses; verified addressed frame permits subsequent unambiguous E5.
- `Iec101SoeBurstAccountabilityTests`: independent *test-only* script peer supplies 60, 500 or 2,000 spontaneous Type 1/COT 3 ASDUs; assert source ASDU count, decoded responses, event ledger row count, checksum/rejection count and zero memory-ring drop with ample configured capacity. These intentionally do **not** pace serial at 1200 bps.

## Physical/independent gates still required

The real 1200-bps 8E1 wire rate is around 109 octets/s (11 bits per character). Frame/ACK overhead, RTU queue capacity, actual COM driver, A/B switching, memory/disk archive and external SCADA traffic determine whether **2,000 generated SOEs** can be delivered before remote FIFO overflow. A 60/500/2,000 synthetic instant peer provides software-path accountability, **not** 1200-bps capacity proof or a guarantee of zero RTU-side loss.

Need a lab setup on the **same exact physical interface/cable/adapter and serial settings** as the comparator tool, with original TX/RX byte evidence, reference RTU event-generation count, overflow indication mapping, host receive/archive counters, and independently reviewed timestamps. Do not automatically reset the outstation, toggle RTS/DTR, retransmit controls, or apply NUC101 to non-NUC projects.

Primary public references: [IEC 60870-5-101 publication](https://webstore.iec.ch/en/publication/23822), [.NET SerialPort documentation](https://learn.microsoft.com/en-us/dotnet/api/system.io.ports.serialport?view=dotnet-plat-ext-8.0), and the repository's [NUC research/limitations](IEC10X_NUC_RESEARCH_20261029.md).

## Bounded in-memory evidence authority

A source audit found an unrelated but material long-session risk: single-link `Iec101MasterSession` used an unbounded `List` for all evidence events; dual-link repeatedly called `List.RemoveAt(0)` after the configured retention threshold, causing O(n) shifting per evicted event.

Both now keep an O(1) sliding `Queue` of at least 100 records, governed by `MaxRetainedEvidenceEvents` (default 10,000). Each memory eviction increments `EvidenceEventsDroppedFromMemory`. The existing real-time `EvidenceReceived` callback still publishes **every** event independently, so memory retention must not be confused with COM RX truncation, RTU FIFO overflow, or confirmation of archival storage. The 60-event small-window fixture verifies 60 live spontaneous callbacks, 100 retained tail records, and the exact evicted count from the sequence ledger. A complete original forensic archive remains a separate opt-in verified recording requirement; this change does not create one.

Do not set the window size below 100 or claim that the in-memory run result includes full history after eviction. Reports should expose the existing eviction counter if the retained window is incomplete.
