# IEC-101 P2E — Evidence-First Failure Timeline

Status: implementation scope 2026-10-28. Continues merged P0/P1/P2A/P2B/P2C/P2D; parent #30.

## User outcome
The engineer can investigate a periodic ~30-second communication pause without sifting through a million raw frame rows. Existing **Capture** shows a short rolling summary; significant anomalies enter the existing **Diagnostics**; full time-series data is persisted as **`timeline-windows.jsonl`**, with original source stream and byte offset of the first observed anomaly.

**Do not claim failover without independent A/B/N﻿UC evidence.** Two serial RX COM ports observe **one** full-duplex serial circuit. Periods of silence can be ordinary idle or standby behavior; the tool states `no traffic observed`, not `link failed`.

## Architecture
```
raw SCADA TX RX0 ─┐
                 ├─ disk-backed binary journals → FT1.2 scanner → P2B transactions ─┐
raw RTU TX RX1 ───┘                                     P2D GI lifecycle ────────────┤
                                                                       P2E FailureTimeline
                                                                       ├─ full timeline-windows.jsonl
                                                                       └─ bounded 64-window UI channel
verified offline binary replay ─→ same FT1.2/P2B/P2D/P2E logic (no COM/TX)
```

The pure Monitor-layer `PassiveIec101FailureTimeline` has no WPF or TX dependencies, no timers and constant working memory. Aggregation uses **5-second windows** by default (range 1–60 seconds). Larger silent intervals are represented by a bounded number of empty buckets; gaps containing more than 12 consecutive empty windows become **one compressed interval** with the exact number of omitted windows. This prevents huge input timestamp jumps from allocating millions of objects.

Each window counts:
- Complete/invalid frame candidates and raw RX counts for each physical direction.
- Link primary function, Class 1 and Class 2 polls, FC9 Link Status requests, secondary FC11 link-status replies and short E5 responses.
- Unsupported/unknown primary FC observations; secondary FC15 `service not implemented` observations (not conflated with COT44–47 ASDU causes).
- Retry, request-response timeout, incompatible/unmatched replies, DFC busy.
- GI Type100 activation, completed GI, abnormal GI phases.
- Original RX stream ID and source offset for first abnormal observation, and a **host timestamp order uncertain** flag.

**Evidence model:** verified byte integrity is distinct from protocol validity, and both differ from a diagnosis. The counts describe what the monitored adapters delivered; timestamp is a USB/Windows host read-completion estimate, not a wire-edge timestamp. A normal standby Link Status exchange is **not a fault**. Never assert an unseen redundant link's activity.

## Performance & maintainability
- Writer publishes all windows to sequential `timeline-windows.jsonl`; a bounded 64-window nonblocking channel is *only* a recent GUI feed. GUI is not the authoritative data source.
- Scratch buffers for scanner outputs, P2B/P2D events, and windows are reused on the single archive-writer thread, avoiding per-serial-read allocation bursts.
- Each observation is O(1) except a bounded O(max 12) empty-window expansion. Memory is constant with capture length; history lives on disk.
- Recompute windows from original verified `manifest.json` + `stream-00.bin` + `stream-01.bin` using the same effective IEC-101 decode profile; deleted secondary indexes are not required.
- UI displays only the last completed observation window in the existing Capture status, not a new window, panel or button. Exceptional windows also produce a concise existing Diagnostics entry tied to source offsets; normal windows do not flood Findings/Event Log.

## Regression and field gates
1. FC9↔FC11 standby supervision, Class1/2 polls and contextual E5: count correctly, **no false fault**.
2. A periodic silence interval of 30 seconds: five-second empty windows, retry/timeout evidence, original byte-offset anchor; do not claim NUC switching.
3. One-hour simulated timestamp gap: O(1) compressed gap with bounded event output; exact skipped-window count.
4. Cross-USB adapter host timestamp regression: explicit uncertainty rather than discarding/reordering valid raw evidence.
5. Unsupported primary FC and FC15 captured and counted, with physical source stream+offset.
6. P2D GI completed, new activation, negative ACTCON: correct GI per-window counters.
7. Full raw writer → derived windows; remove all derived indexes → verified offline replay recomputes same frame/service counts, capture remains SHA-valid.
8. WPF MainWindow smoke for no missing operator controls, profile replay, Frame Trace/Findings/GI unchanged.
9. Release warnings-as-errors, full xUnit, 19 protocol smoke, actual Windows WPF smoke, CodeQL, dependency, hygiene, portable Win-x64 artifact + checksum.

## Remaining after P2E
This is a *forensic timeline foundation*, not a complete multi-day paged search UI or automatic root-cause proof. Future P3 requires four RX channels for A/B, indexed seek and linked transaction jump-to-byte, NUC event integration, longer hardware soak, and electrical UART timing analyzer for 33-bit idle proof.
