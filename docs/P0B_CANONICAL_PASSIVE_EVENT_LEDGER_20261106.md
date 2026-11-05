# P0-B — Writer-owned IEC-101 process event ledger (2026-11-06)

**Implementation:** PR #92, following P0-A #91 and parent [Issue #39](https://github.com/masarray/ARIEC60870/issues/39). This document describes implementation scope, not certification or field FAT.

## Root cause fixed

Prior IEC-101 passive Event Log source observations were projected only after `PassiveMonitorSession._frames` (bounded 512, `DropOldest`). At high traffic this could silently omit GUI SOE rows despite valid frames being handled by the complete writer. A bigger UI queue is not a correctness fix.

## Single source of accepted process observations

- `PassiveIec101ProcessEventClassifier` is owned by the **single capture writer**. It accepts only verified FT1.2 Variable frames from PRM=0 with an explicit selected/verified link/address profile, canonical IEC-101/104 ASDU decoding, complete objects, a standard supported process Type, and source-qualified object ordinal. Unsupported, incomplete, primary TX and invalid checksum never become process facts.
- `Iec10xProcessEventPolicy` lives in **Core** and is shared by active Master/replay observation identity and passive writer; no second IEC101 value decoder or independent COT policy is allowed.
- Per-object `PassiveProcessEventCandidate` records observed Type/COT/CA/IOA, quality, value, CP timestamp, TEST/NEG and known source-state transition. This is an *observed application object*, **not** proof of a physical alarm, remote command execution, or FIFO continuity.
- Capture writer appends an indexed `PassiveProcessEventFact` before publishing the same frame to lossy GUI. Identity uses source stream + original byte offset + object ordinal + session scope. Repeated spontaneous observations are distinct facts; duplicate logical IOAs from A/B are not silently merged.
- The temporary per-session, append-only binary index has an explicit 1 GiB storage ceiling. On I/O/quota failure, every later accepted object is counted as **unindexed** and integrity becomes uncertain. No silent eviction, blocking GUI or invented successful decode.
- With explicit full raw recording ON, `process-events.jsonl` adds durable typed source references to the separately integrity-verifiable raw journal. With recording OFF, no permanent `.ariec` is created: the temporary index remains only until the next session/close, like the existing temporary Trace history.
- `PassiveProcessEventPreparation` reads pages and verifies the original envelope + ASDU using Core off Dispatcher; WPF Event Log consumes verified writer facts rather than independently deriving events from `_frames`. Values and Trace can continue to sample/display previews without suppressing source capture.
- The Event Log stays a **420-row bounded visible window**. When it falls behind, skipped older visible rows are counted and disclosed; the original indexed session facts are not silently overwritten. UI historical event pagination is not in this PR. Do not call a visible-window count the number of events physically received.
- Optional manifest counters: `AcceptedProcessEventObjects`, `IndexedProcessEventObjects`, `UnindexedProcessEventObjects`, `PreviewFramesDropped`. These are separate from UART driver errors, raw-byte count and external RTU/SCADA FIFO delivery.

## Acceptance criteria

1. Run 640 synthetic valid IEC-101 Type31 frames x 4 objects = **2,560 writer-owned process facts** with only 512 visual-frame slots. The index and optional full-session typed file must retain all accepted objects, with exact stream/offset/ordinal and page cursors.
2. Corrupt checksum/partial frames, primary PRM=1 and unsupported Type must never be promoted; bounded index capacity must fail observably with retained earlier facts.
3. Core, Master, Monitor, Desktop and real-WPF acceptance verify canonical semantic parity and that a visual frame does not double-add an indexed process event.
4. No Master TX/polling/FCB/GI behavior change; no Passive TX.
5. Exact-head Windows Release build, smoke, xUnit, WPF acceptance, CodeQL, dependency and hygiene must all pass before merge.

## Not proven and follow-on work

- Temporary event index is not a permanent source archive. Its 1 GiB cap or disk failure must be reported and later records may be unindexed; captured bytes may also be absent due to upstream driver/RTU/hardware losses.
- No claim about recovery of the SICAM A8000 physical ~2,000 SOEs or CP8050 FIFO buffering. Real 1200-bps dual/four-COM electrical FAT is a separate gate.
- IEC-103/104 Master event ledger parity, historical Event Log pagination, unified incremental FT1.2 byte-stream framing, negative conformance corpus and performance/soak measurements are later architecture gates. Do not conflate automated synthetic burst throughput with sustained hardware throughput.
