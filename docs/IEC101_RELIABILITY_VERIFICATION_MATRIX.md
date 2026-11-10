# IEC-101 Reliability — Deterministic Verification & FAT Matrix

**Status:** proposed acceptance vectors, not executed test results. **Tracker:** [P0 issue #143](https://github.com/masarray/ARIEC60870/issues/143). **Implementation authority:** [IEC101 reliability plan](IEC101_RELIABILITY_IMPLEMENTATION_PLAN.md). This document is a reproducible *software + physical* gate, **not a conformance certificate**.

## Evidence grade and test conventions

- **A — Unit fixture:** synthetic byte stream, controlled virtual monotonic clock, deterministic fake transport and assertion on emitted exchanges/FCB/TX.
- **B — Independent peer:** at least one independently implemented IEC101 responder or independently generated reference vectors; no borrowed/copied proprietary code.
- **C — Windows hardware-in-loop:** real Windows COM driver, serial adapter/converter and instrumented wire-side RX/TX at 1200/9600 where supported.
- **D — Physical RTU FAT:** isolated authorized field-equivalent outstation and identical adapter/wiring/port cross-comparison. No production control/reset.
- **Release verdicts:** `NotImplemented`, `FixturePass`, `IndependentPeerPass`, `HardwarePass`, `Unknown`, `Failed`. **A ≠ D**.

Test run metadata: exact Git SHA, test build, machine/OS, RTU CPU/module/firmware when permitted, physical port identity (not only COM number), adapter chipset/driver, electrical mode/pinout, baud/data/parity/stop/handshake/RTS/DTR, link address width/value, COT/CA/IOA widths, active/passive, source A/B, selected convention version, UTC and monotonic timestamps, raw TX/RX capture, sanitized output, result + reproduction steps.

## A. Byte/stream and response correctness (R01–R07)

| ID | Scenario / injected fault | Required observable acceptance | Minimum grade |
|---|---|---|---|
| L01 | FC9 status → valid addressed FC11 | Only requested status accepted as link proof; exact raw bytes retained | A+B |
| L02 | FC11 after FC3/GI, FC10 or FC11 class poll | OutOfContext, never GI/Class success, no FCB advance | A+B |
| L03 | Wrong PRM/address/source/epoch with checksum valid | Preserved evidence but rejected for pending exchange | A |
| L04 | FC1 link-layer NACK vs application COT negative | Separate negative outcomes; no false ACTCON/ACTTERM | A+B |
| L05 | FC10/FC11 → FC8 data, FC9 no-data | Correct service mapping; no-data not counted as physical disconnect | A+B |
| L06 | Context-legal E5 after confirmed data/poll | Unambiguous permitted interpretation; original byte remains E5 | A+B |
| L07 | E5 before any addressed proof / FC9 only | Never establishes RTU identity or valid link-status FC11 | A |
| L08 | Unsupported short A2; noise / TX echo / invalid checksum | No fabricated IEC101 NACK; raw evidence + explicit diagnostic | A+B |
| L09 | Split variable frame over many RX chunks and long 1200-bps inter-byte gaps | One complete frame with exact bytes and no premature expiry | A+C |
| L10 | Truncated 0x68 frame, then fresh E5 or valid fixed frame | Partial frame expires with counted loss; parser re-synchronizes, neither sticks forever nor borrows old request | A+C |
| L11 | Delayed old FC11 followed by current FC9/FC11 | Original timing/source retained; cannot falsely complete wrong exchange | A+B |
| L12 | Corrupted length L1!=L2, overflow length, zero/1/2 address-width variants | Safe bounded parser and explicit invalid evidence | A |
| L13 | Read returns bytes, but no complete frame; 0 byte read; COM RX=0 | Distinguishable transport byte count vs parser frames vs accepted reply | A+C |
| L14 | Stop/cancel with read/write pending; driver disconnect | No orphan worker/COM lock; no stale emission after stop | A+C |

## B. Transaction / FCB / command safety (R01–R04, R09–R10)

| ID | Scenario | Required observable acceptance | Minimum grade |
|---|---|---|---|
| T01 | Positive matching confirm after FC3/FC10/FC11 | Exactly one legal FCB advance for matching FCV sequence | A+B |
| T02 | Lost ACK on eligible read/poll; bounded resend | Retry policy obeys standard/profile; same original request/FCB when retransmission is eligible | A+B |
| T03 | NACK/DFC/bad checksum/unrelated reply | FCB unchanged; explicit outcome | A+B |
| T04 | Reply arrives after deadline while next service scheduled | Do not falsely correlate; explicit stale/ambiguous evidence | A+B |
| T05 | Physical control Select/Execute uncertain after driver write | No automatic replay or execute after failover; UnknownDelivery verdict | A+B |
| T06 | Queue command before link verified and on busy/standby | Rejected at admission, bounded queue and telemetry | A |
| T07 | Command queued on A then A fails and B promoted | Never transmitted on B silently; original owner/epoch invalid | A+B |
| T08 | SBO negative ACTCON, absent ACTTERM, feedback ordering variants | Correct source-qualified staged verdict; no inferred mechanical success | A+B |
| T09 | Clock sync response missing after write accepted | No automatic repeated clock sync; GI may be deferred only if it was **not sent** | A+B |
| T10 | Repeated Stop/Start, old queued commands and previous epoch frame | No prior-epoch command or stale evidence accepted | A+C |

## C. Scheduler, GI, ACD/DFC and A/B (R08–R11)

| ID | Scenario | Required observable acceptance | Minimum grade |
|---|---|---|---|
| S01 | ACD=1 bursts of Class1, then ACD clears | Bounded drain stops at no-data/clear/time/frame budget; no endless Class1 spam | A+B |
| S02 | Continual ACD=1 plus Class2 pending | Fairness: Class2 gets allowed polling within documented bounded budget | A+B |
| S03 | DFC=1 for multiple cycles → DFC=0 FC9 | Only status supervision during busy; no GI/Class/commands until ready | A+B |
| S04 | GI link ACK, ACTCON, objects, ACTTERM | Separate correctly ordered typed stages and covered/unknown IOAs | A+B |
| S05 | GI ACTCON negative / ACTTERM absent / only partial objects | GI never reported complete; values stay partial/unknown | A+B |
| S06 | A fails, B has only stale last-good proof | No unsafe promotion or command replay | A+B |
| S07 | Fresh B proof, A fails, anti-ping-pong and manual failback | Single active owner, deterministic transition, source-qualified images | A+B+C |
| S08 | A/B both healthy, Link A standby | No Class 1/2, GI, clock or mutating command on standby unless specifically profile-allowed | A+B |
| S09 | Promoted B post-switch GI opt-out / opt-in | Configurable project rule; no assumed universal NUC GI | A+B |
| S10 | Active switched during heavy SOE, stale image status | Original per-source facts retained; image stale/ready accurately reflects proof | A+B |

## D. Serial throughput, faults and observability (R05–R07, R14–R16)

| ID | Scenario | Required observable acceptance | Minimum grade |
|---|---|---|---|
| P01 | 1200 bps 8E1, short FC9 and 250-byte frame | Distinct first-byte vs completion budgets, no premature full-frame timeout | A+C |
| P02 | 1200 bps 60/500/2,000 SOE with ACD drain | Original valid event counts + overflow evidence reconciled with RTU; no silent pipeline loss | A+C+D |
| P03 | RTU FIFO overflow, local serial overrun, DFC, measurement OV | Four different typed facts; never conflate | A+D |
| P04 | CTS/DSR/DCD transitions under None vs RTS/CTS | Correct diagnostic; no silent RTS/DTR/baud/parity changes | C+D |
| P05 | COM unplug during read, reconnect while queue pending | New epoch, bounded permitted auto-recovery, no hidden manual second Start, no mutating replay | A+C |
| P06 | TX-only FC9: RTU LED TX/RX visible but Windows RX=0 | Precisely distinguish no driver RX from framing/transaction mismatch | A+C+D |
| P07 | Identical adapter/cable/port with IEC_Test and ARIEC (one at a time) | Capture exact raw TX/RX and modem lines, document unexplained differences | C+D |
| P08 | 1-h automated stress + extended physical soak | Stable bounded memory/task/queue depth, throughput and explicit loss counters; no hang | A+C+D |
| P09 | GUI/RX high load, report export during capture | No blocking UI on COM reader; store and UI projections show truthful loss/health | A+C |

**Rate model:** at 1200 bps and 8E1, ~11 bits/character gives ~109 bytes/s before overhead; a 250-byte frame needs ~2.29 s serialization time. Measured throughput, Class polling gaps, RTU FIFO capacity, cable latency and actual line settings determine field pass/fail. A `2,000 SOE in seconds` requirement is unsupported at this baud rate.

## E. M0–M5 exit conditions

| Gate | Pass condition | Evidence owner |
|---|---|---|
| M0 | Latest-master/PR136 conflict audit, red fixtures with links and exact baseline SHA | P0 owner |
| M1 | All L01–L08 and T01–T10 fixtures including single/dual parity, **zero false acceptance/unsafe replay** | Shared engine owner |
| M2 | L09–L14, P01/P04/P05/P06: one reader, bounded port ownership, monotonic timing and retained driver bytes | Transport owner |
| M3 | S01–S10, P02/P03: no starvation/spam/stale failover, source-qualified GI/command verdict | Scheduler/A-B owner |
| M4 | Versioned profile fail-closed tests, meaningful diagnostic confidence, no serial guesswork | Profile/evidence owner |
| M5 software | All deterministic and independent peer vectors plus Windows full CI exact-head; documented measured perf/soak, zero silent dropped critical facts | P0/release owner |
| M5 field | Isolated RTU, same physical COM/wiring/adapter comparator, modem evidence, 1200-bps burst + recovery + A/B FAT; approved signed log | Field engineer |
| Public RC | New prerelease only after software gate with limitations explicitly disclosed; no tag rewrite | Release owner |
| Production/multi-vendor claim | Separate documented field certification and scope review; NOT implied by green CI | Product/release owner |

## F. Reproduction and CI workflow

- Keep fixtures small, sanitized and committed (original source frame bytes + expected typed outcome), no customer capture or proprietary standards text.
- Run focused Master/Core xUnit + protocol smoke **once per logical change**. Avoid launching multiple redundant CI runs. Full Windows Release/format/all xUnit/WPF acceptance/CodeQL/dependency/repository-hygiene only on coherent integration head.
- Every failing case links to issue #143 with `case ID, seed, primary FC, serial settings, raw physical or simulated TX/RX, expected vs actual, commit SHA, failure category`.
- At integration: verify protocol and cross-protocol evidence (IEC103/104 unchanged), Windows artifacts and SHA256. **CI never substitutes for physical SICAM FAT**.
- Physical capture: compare Windows-side COM **identity** and device-side circuit, not just ports labeled COM4/COM5. Never perform unauthorized live RTU reset, clock sync or physical control.
