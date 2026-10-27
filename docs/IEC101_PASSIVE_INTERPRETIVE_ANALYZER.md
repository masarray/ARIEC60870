# IEC-101 Passive Monitor — Interpretive Analysis Architecture (ASE2000 V2 benchmark)

Status: implementation PR baseline · 2026-10-28. Related issue #19 and passive P0 engine.

## Fundamental correction

Passive is **transport behavior (receive-only)**, not a reduced user interface.
The same captured SCADA↔RTU conversation must be exposed through the existing:
- **Messages** — compact exchange/message summaries with direction, timestamp and link function.
- **Frame Trace** — fully reconstructed FT1.2 telegrams, raw HEX + interpreted Link Control and IEC-101 ASDU fields.
- **Events** — observed IEC-101 spontaneous event/changed IOA log, with event timestamps when present.
- **Values** — current ASDU IOA values with quality, type and cause of transmission, across multi-object ASDUs.
- **Capture** — raw capture health, bytes/COM, recorded file path and integrity check.
- **Report / Findings** — existing read-only analytical/report views, with known limitations until specialized passive correlation is implemented.

No separate window, no separate nav button, no Master command engine instantiated. User selects the existing Setup dropdown IEC-101 Passive Monitor, then Record RX.

## Evidence flow

    SCADA TX → COM1 RX ┐
                       ├─> PassiveMonitorSession → raw binary journal (authoritative)
    RTU TX   → COM2 RX ┘                          └─> independent FT1.2 per-direction scanner
                                                    ├─> frames-00/01.jsonl (byte-offset, timestamp, checksum metadata)
                                                    └─> bounded live UI feed
                                                          └─> existing IEC-101 ASDU decoder
                                                                ├─> Frame Trace / Messages
                                                                ├─> Events
                                                                └─> Values

**Never decode the 48-byte preview sample**: variable FT1.2 frames may exceed a USB read chunk or the preview cap. A scanner must retain partial bytes across read boundaries and consume every written raw chunk, including E5/A2 ACK/NACK and corrupted/unknown bytes. The binary raw journal remains the only source guaranteeing byte preservation; UI frame feed is bounded for responsiveness. Frame index records are derived and can be recreated from the raw journal later.

**Do not use the Core.Ft12Parser.Asdu decoder for IEC-101**: that parser is bound to the IEC-103 ASDU format. Derive FT1.2 boundaries from the independent passive stream scanner; pass user data to existing Master.Protocol.Iec10x.Iec10xAsduDecoder (pure decode) in the Desktop projection. Long term this pure decoder can be relocated to shared Core without creating two competing implementations.

## Operator contract

- Setup shows both physical directions, baud/serial framing and an expandable set of IEC-101 interoperable width fields: Link 0/1/2; COT 1/2; CA 1/2; IOA 1/2/3. Defaults are profiles, **not guesses verified against a device**.
- Active Master-only GI, Class polls, clock sync, commands and RTS/DTR transmission controls remain hidden and unreachable in Passive Monitor.
- Recording may start with COM1 and COM2 receive only; changing active tabs never stops journaling.
- Frame Trace shows every observed FT1.2 candidate regardless of Full/Balanced/Proof suppression configured for active Master. Incorrect checksum frames remain visible and cannot update point values.
- Message timestamps are Windows/USB read-completion approximations; cross-port ordering/response latency are not physical bit-precision.
- Events tab distinguishes **observed spontaneous ASDU** from derived state changes. First observed spontaneous status can be an event even without previous value. A status-only standby link is not automatically classified a failure.
- Point Values updates all IOAs in a multi-object ASDU, not only first decoded IOA. Unsupported type remains raw/unknown, not fabricated.
- A single fully monitored duplex link requires 2 COM ports; reliable A/B redundant-link switching observation needs 4 COM inputs and remains P3.
- The older raw preview has moved to **Capture**, as optional inspection/health; it must never replace Frame Trace or hide Events/Values.

## Test strategy

1. Byte-level deterministic scanner test: frames 10 / 68 / E5 / A2; two-byte link address; split read across arbitrary boundaries; corrupt checksums/noise; bounded re-sync.
2. Release build and existing Master regression must stay green.
3. Windows GUI smoke opens actual MainWindow, changes passive→active→passive and verifies Trace, Events, Values, Messages and Capture still navigable, with command dock hidden in passive.
4. Device FAT: capture both TX lines through verified non-intrusive RS-232 taps, compare telegrams to a trusted reference, verify ASDU field width profile; observe multiple ~30 s cycles.
5. Offline exact reprocessing of archived raw journals and deeper request-response correlation are still tracked improvements. Do not claim full ASE2000 equivalence or a physically verified zero-loss capture from simulated tests.

## ASE2000 V2 manufacturer benchmark, independent implementation

- https://www.ase-systems.com/products/ase2000-v2/ — Line Monitoring supports interpretive/raw styles, Messages View and Point Values.
- https://www.ase-systems.com/wp-content/uploads/2021/06/ase2000-communication-test-set.pdf — §§6, 7, 8.4 Line Monitor, 8.5 Messages View, 8.6 Event Log, 8.2 Point List. The manual distinguishes the live communication source from the viewing modes.
- Do not copy vendor source, design assets or screenshots. Generic USB UART COMs do not reproduce proprietary Line Analyzer electrical timing capability.
