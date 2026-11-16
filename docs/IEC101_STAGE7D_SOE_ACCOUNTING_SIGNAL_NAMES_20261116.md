# IEC-101 Stage 7D — SOE object accountability and Signal List trust

Date 2026-11-16. Base `master dcda97ed6c567835a32db08896ea57ef03b07208`.

## Verified root causes

- `Iec101MasterSession` single-link retained all `_events` in an unbounded list for the full master run. Dual-link bounded its list using `RemoveAt(0)`, an O(N) shift on every over-capacity event, which gets worse under SOE bursts. These are in-memory reporting buffers, not authoritative persisted raw-wire journals.
- Legacy `DpiEvents` increments per decoded ASDU, not per information object. A multi-object ASDU must not be miscounted as one observed digital event. Master and GUI receive processing already project the first object and sibling objects individually.
- The approved FEEDER CB STATUS is explicitly mapped as CA=105, IOA=16712689, TI31. The archived `.ariec` view accepted a `names` index but constructed `SemanticLabel` from the saved display caption rather than the canonically decoded CA/IOA/TI and active Signal List.
- Value name refresh after loading/editing the Signal List allowed read-only CA aliases without checking whether stored observations from the **same physical source** had multiple CAs. That could mislabel another station.

## Solution

1. Use one O(1) bounded `Iec101BoundedEvidenceHistory` in single and dual Master with the configured retention cap (minimum 100), exact memory-eviction counter and unchanged `EvidenceReceived` callback order. This does not drop serial RX bytes before decoding and does not replace a full wire recorder.
2. Count one `ObservedSpontaneousDigitalObjects` per **accepted, decoded IEC-101 RX process object**, including sibling IOAs within SQ0/SQ1 ASDUs, with positive spontaneous COT3, valid header and no test bit. This new report metric is kept separate from legacy ASDU counter `DpiEvents`. Counts are observations on the accepted Master link, **not RTU-generated total or source FIFO size**.
3. On Signal List refresh, only borrow a unique read-only IOA name from another CA if the Value came from a real observed source with one CA. With multiple observed CAs on the same source, use strict matches only. Never rename or remap a physical address, command target, decoder, GI coverage or archived raw frame.
4. Offline IEC-101/104 `.ariec` rows are decoded **once** using their recorded widths. A first linear pass establishes per-source/IEC-104-connection trusted CA uniqueness; a second bounded pass builds display rows from the cached typed source facts. Name-only CA recovery stays read-only, unique and process-only; multiple trusted CAs on one source disable aliases. Malformed/warning frames are not admitted to mapped proof. Original archive remains unchanged.

## Deterministic tests

- Feeder CB Status TI31 or TI3 with CA 1 versus profile CA 105 displays its unique name and unchanged raw CA/IOA.
- One source with CA1+CA2 does not borrow an unrelated station name; distinct physical A/B sources are independent.
- Unverified seeded placeholders may use strict profile CA but cannot create aliases.
- Captured `.ariec` with authentic FT1.2 bytes recovers the name; multiple trusted CAs on the **same physical source** disable name-only aliases while strict matches remain available. Ambiguous multi-station IOA stays unassigned.
- Two-object spontaneous digital ASDU counts **two** objects; GI COT20, Test bit or RX Warning counts zero.
- Stress counters and bounded memory for 60, 500 and 2,000 synthetic received SOE observations with exact eviction accounting.
- Full Windows build, formatter, protocol smoke, all xUnit, CodeQL and master WPF acceptance remain required.

## Field acceptance pending

Still **not physically verified**: independent RTU/IEC_Test baseline on identical COM wiring, 1200-bps Siemens SICAM CP8050/CI8551 serial data, 60/500/2,000 RTU-generated SOE parity, FIFO overflow evidence, serial fault/reconnect, vendor-independent testing. Software observation count cannot establish events that the RTU never exposed on the wire. No automatic polling-rate changes or mutating RTU commands.
