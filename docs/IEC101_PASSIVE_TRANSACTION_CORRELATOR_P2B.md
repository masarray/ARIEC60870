# IEC-101 Passive Monitor — P2B Transaction Intelligence (implementation 1)

Architecture date: 2026-10-28. Parent issue #27. Builds on merged P2A replay, P1 common Trace/Values/Events and P0 byte-accurate recording.

## Observable truth versus inferred diagnosis

IEC-101 Passive observes **both transmitter directions of one full-duplex serial link** on independent RX-only COMs. It does not send/reset/poll, does not know what an *unobserved* redundant SCADA line is doing, and cannot time physical bit edges using typical USB serial adapters.

- **Confirmed observations:** exact validated link-control bytes, PRM, FCB/FCV, ACD/DFC, request functions, response functions, raw direction+source offset, checksum/incomplete status.
- **Correlated:** a request+compatible reply with matching link address and opposite physical direction, within an explicit host timestamp window; positive/negative reply, retry and timeout classification.
- **Caution:** host receive timestamp delta is an estimate and may be unavailable for negative inter-port skew. E5 is a **contextual short response**: it can stand for FC0 ACK after confirm, or FC9 no-data after Class 1/2 poll where permitted and ACD/DFC need not be carried. E5 has no physical function or address field.
- **Hypotheses:** a ~30-second SCADA failover or repeated link-status polling may point to switching, but is NOT evidence of the unobserved alternate link's status. A 4-RX four-direction upgrade remains P3.

## Architecture: data plane separate from GUI

    SCADA TX COM ─→ raw journal writer ─→ per-direction FT1.2 scanner ─┐
                                                                      ├─→ PassiveIec101TransactionCorrelator
    RTU TX COM ───→ raw journal writer ─→ per-direction FT1.2 scanner ─┘     ├─→ transactions.jsonl (derived, durable)
                                                                            └─→ bounded GUI notification feed → existing Findings
    raw stream-00.bin + stream-01.bin + manifest.json ─→ P2A verified replay ─→ same correlator → existing Findings/Trace

**Important:** correlate in the **writer**, not against visible WPF Frame Trace or its bounded 512-frame presentation queue. Raw journals remain authoritative and transaction JSONL is a recomputable derived index. GUI must never block real serial reads or disk capture.

The correlator is a self-contained `ARIEC60870.Monitor` state machine (no Active Master dependency, no WPF and no transmit API). State uses a key `(primary transmitter stream, link address)`, bounded by a configurable maximum tracked link count. Only one outstanding request is tracked per link key under this first unbalanced/balanced-observation contract; second outstanding requests are classified as superseding or retry, not incorrectly paired. E5 has no link address, so it is **not matched arbitrarily** if more than one eligible pending request exists. A2 is forbidden by IEC-101 and must never resolve a transaction.

## Protocol rules / acceptance

| Primary control | Compatible secondary observation |
|---|---|
| FC0 Reset Remote Link / FC1 Reset User Process / FC7 Reset FCB | ACK FC0, NACK FC1, service error FC14/15 or single character ACK/NACK |
| FC3 Confirmed User Data | ACK/NACK (same constraints) |
| FC4 Unconfirmed User Data | No reply required; do not open timeout transaction |
| FC9 Request Link Status | Secondary FC11 Link Status, negative/service-error response |
| FC10 Request Class 1 | Secondary FC8 User Data or FC9 No Requested Data; negative/service-error |
| FC11 Request Class 2 | Same FC8/FC9 rules |
| E5 | Addressless short ACK or short FC9 no-data, meaning inferred only from one compatible opposite-direction pending request; not valid for FC9 link-status request |
| A2 | Forbidden under IEC-101; retain exact byte and emit protocol violation, never accept as NACK |

An ordinary standby Link Status request/response is **normal traffic**. ACD=1 and DFC=1 are reported accurately without equating ACD to a fault or DFC to a positive ACK. Requests with same FCV/FCB and identical raw bytes while still pending are explicitly classified as retries (original start time retained). Reused confirmed FCB without an observed reset is *cautionary*; capture may start mid-link session.

Any malformed/checksum-invalid frame, incorrect reply function or mismatched link address must not advance pending transactions. If a raw archive ends while requests remain, classify `PendingAtEnd` (not a manufactured timeout). A timeout means *no compatible response was observed by the configured host time bound*, not proven physical line failure.

## Reliability and performance

- Bounded state: configurable 1–1024 tracked links, default 64, with explicit capacity-eviction diagnostics. Constant or bounded memory independent of capture duration; incremental event emission (no growing transaction list).
- Writer stores all derived transaction events to `transactions.jsonl` and exposes a separate bounded UI event channel. A slow GUI cannot interfere with raw acquisition.
- GUI coalesces repeated (kind, link address) findings over 3 seconds and caps at 20 displayed findings per second, preventing the shared Findings queue from growing under noisy traffic; **no events are discarded from the derived on-disk journal by GUI throttling**.
- Existing Findings (not a new window) shows actionable anomalies with exact RX stream and raw byte offsets; regular successful polls do not flood the Findings view.
- Read-only offline replay builds findings again from full verified journal, with the exact field widths and link mode recorded in the capture manifest.
- No proprietary ASE2000 V2 code or resources are copied. UI continues using existing ARIEC60870 design tokens.

## Caveats / next hardening

- Two COMs cannot prove A/B failover on an unmonitored second link.
- USB inter-port read-completion order is not physical wire order: near-simultaneous responses may be uncorrelatable; do not fabricate high-precision latency.
- Multi-outstanding transactions on the *same* IEC-101 link address and balanced simultaneous primary actors require a future carefully modeled multi-pending policy; current implementation reports supersession.
- Event journal and raw journal are preserved independently; long-term replay index/paging, report table for all transaction pairs, and physical SICAM/ETERA FAT remain tracked work.
- No two-hour physical soak or source-device timing validation has yet been performed.

## Gates / regression cases

Required xUnit: fixed/variable, E5 context-specific FC0/FC9, forbidden A2, Link Address size 0/1/2, Class1/2, FC9 Link Status, ACD/DFC, valid/no-data/NACK/Busy, corrupt checksum, foreign link address, primary-role contradiction, duplicates/FCB/FCV reset, bounded capacity, explicit timestamp uncertainty, timeout and replay parity with optional derived index removed. Require Windows Release 0 warnings/errors, protocol smoke, real WPF startup, CodeQL/hygiene/dependency gates and portable self-contained artifact before merging.

For physical validation: archive enough complete consecutive cycles on *both* directions of an isolated tap. If investigating redundant A/B switching, instrument both circuits with four RX ports and do not assume the alternate link's state from a single monitored cable.
