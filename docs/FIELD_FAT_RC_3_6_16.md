# Physical IEC-101/104 FAT — v3.6.16-rc.1 execution sheet

**Release candidate only; field verdict UNKNOWN until real RTU evidence exists.** Target reference: SICAM A8000 / Siemens RTU family where explicitly configured, but the actual device variant, serial card, firmware, local IOA map and cable wiring must be documented. Prior site behavior was not laboratory proof for this new build.

## 0. Safe, bounded preflight

1. On an authorized offline/isolated RTU line, install ArSee `v3.6.16-rc.1` and record the release SHA from the published tag, Windows version, adapter make/chipset/driver/physical serial port, RTU CPU/module/firmware, time and independent reference tester. **Never use the RTU's production SCADA link as a second active Master.** No switching, clock sync, setpoints or remote command tests without an independent written test permit.
2. Confirm the real IEC-101 link mode, device LA and length (0/1/2 bytes), CA size/value, COT size, IOA size, baud, parity, stop bits, handshake and actual TXD/RXD/GND/RTS/CTS wiring. The reference `CA=105`, `1200 bps` and example RTU I/O points below **are not universal defaults**. Log each configured value, but do not put private addresses or credentials into public GitHub issues.
3. **Choose one mode:** active IEC-101 Master **only** with independent link authorization, or passive/receive-only monitoring through approved isolated high-impedance taps. For a one-line bidirectional passive trace, two independent RX adapters are needed; for redundant A/B line capture, four RX adapters. Opening a serial port may toggle modem lines: software RX-only does not itself guarantee electrical isolation.
4. Back up the original project Signal List. If old names are present, review `Signal List → More → Sync 31 I/O reference... → Save As`. No automatic profile overwrite.
5. Open Signal List → More → **Export blank Field FAT worksheet...**, save CSV separately from the JSON profile. The export is one row per point, with all results `NOT TESTED` and blank physical evidence fields, even if the source profile has TwoPoint settings. It reads current unsaved edits but does not save the JSON. Record the profile filename/hash and tester identity separately.

## 1. Core FAT matrix

| ID | Mode / operator procedure (no unapproved TX) | Minimum evidence needed | Field verdict |
| --- | --- | --- | --- |
| F01 | Check installer startup, v3.6.16-rc.1 title, Signal Map search by Bay/IOA and scaling tabs | Screenshot with version, 31-point profile count, selected CA/IOA/TI, archived original JSON | NOT TESTED |
| F02 | Authorized isolated IEC101 connection: Status FC9; observe addressed PRM=0 link evidence, FC0 reset only if link owner explicitly permits | Actual TX/RX raw frames, LA width/value, short E5/fixed FC9/FC11 classification, host COM byte/frame counters | NOT TESTED |
| F03 | GI 100 / COT6: ACTCON7, objects COT20–36, ACTTERM10 where RTU supports; no false 'complete' from mere bytes | GI #, time, direction, source, CA/QOI, expected/observed unique IOA count, missing/unknown reason, HEX refs | NOT TESTED |
| F04 | FEEDER Q TI11 IOA790447, KOPEL Q TI11 IOA790439, TRAFO Q TI11 IOA790443; observe baseline then **negative/zero/positive only if physically attainable safely** | For each actual point: raw signed I16, independently read RTU/SCADA MVar, QDS/COT, approved engineering scale file/approval and original HEX | NOT TESTED |
| F05 | TI9 TRAFO SOGI POAQ IOA790449 (earlier source 0..1000; **later operator-corrected feedback 0..1200**): confirm actual SCADA database revision and independent measurement; do not equate with POOP command | NVA decoded fraction vs candidate 0..1200 display, independent comparator, QDS, and distinct TI48 POOP 0..1000 command metadata; no TX | NOT TESTED |
| F06 | TI13 IEEE float active P IOAs 790446, 790438, 790442; compare with RTU/SCADA when raw float is already MW | Wire-decoded IEEE float, rendered MW, independent comparator, no double CT/PT scaling | NOT TESTED |
| F07 | Compare Values, Event Log, raw Trace, CSV/report and verified replay for identical frames/IOAs | Identity CA/IOA/TI/COT/QDS, original payload unchanged, timestamps/source and late/invalid quality disclosed | NOT TESTED |
| F08 | Controlled SOE batch 60, 500, and 2,000 **only on authorized isolated bench** and as the RTU can safely sustain; note 1200bps line capacity | Event injection source tally, emitted RTU FIFO, wire-valid received count, dropped/overflow/ACD/DFC, final timeout and 2-way capture hashes | NOT TESTED |
| F09 | Driver COM pause/unplug/replug, Start–Stop–Start and A/B standby→active path only on disconnected laboratory link | No stale port lock, no ghost command retry, source-specific link epoch, typed stale/timeout outcome, no unexpected TX | NOT TESTED |
| F10 | Inspect command-capable points (RCD/RCA), feedback and policy **without executing physical control** | Correct distinct command IOAs and feedback families, select/execute availability not confused with proof of mechanical action | NOT TESTED |

A row may be `PASS` only when an independent engineer supplies source-specific evidence. Otherwise record `FAIL`, `UNKNOWN` or `NOT TESTED`, not a fabricated pass. Any real TX, Reset Remote Link, GI, action or link disconnect needs explicit authorization. Keep the software passive when attaching to an operational SCADA connection.

## 2. Field profile spot checks (CA=105 sample ONLY)

| Signal | IEC TI | IOA | Source scaling expectation | What is actually established |
| --- | --- | ---: | --- | --- |
| FEEDER TM Q | 11 | 790447 | Signed I16 counts | MVar conversion unknown; obtain gateway specification |
| KOPEL TM Q 1 | 11 | 790439 | Signed I16 counts | MVar conversion unknown; obtain gateway specification |
| TRAFO TM Q 2 | 11 | 790443 | Signed I16 counts | MVar conversion unknown; obtain gateway specification |
| TRAFO SOGI TM POAQ | 9 | 790449 | Operator-corrected feedback: decoded NVA X0=0, X100=1 -> Y0=0, Y100=1200 (provisional MW display) | Earlier documents show 1000; revised range **not independently verified at this RTU** |
| FEEDER/KOPEL/TRAFO TM P | 13 | 790446 / 790438 / 790442 | IEEE short float Factor identity when transmitted in MW | Unit claim requires independent configured gateway comparison |
| Other TSD/TSS/RCD/RCA | 30/31/46/47/48 etc. | From local Signal List | Observe states and feedback as specified | No automatic physical control permission implied |

Do not compute Y0/Y100 for Q from CT 2000/1 and PT 500/0.1 kV alone. A nominal 3-phase MVA limit is **not** the approved IEC raw-scale coefficient. Never assume all 31 points arrived because GI produced one ACTTERM.

## 3. Independent comparator and evidence packaging

- Prefer exactly the **same physical serial converter, cable and RTU port** for independent tester and ArSee in separate sessions; log interleaved operation, wiring changes and handshake mode. If two testers use different physical links, label comparison non-equivalent.
- Save the approved site profile as a new JSON and capture the original raw files; never alter raw HEX or quality to make scaling look correct. Save read-only `.ariec` or independent raw `manifest.json` + `stream-*.bin` journals with SHA-256. Capture and CSV/report are separate projections, not replacements for original physical evidence.
- Record `Get-FileHash -Algorithm SHA256 .\capture.zip` and `Get-FileHash -Algorithm SHA256 .\approved-profile.json` in the FAT report. Export all relevant trace rows with event timestamps, source, direction, CA/IOA/TI, COT, QDS and conversion settings.
- Record issue references when FAIL/UNKNOWN, including physical data rate, RTU overflow counters and **which stage** failed (Windows driver bytes, frame parser, transaction, GI, mapping, display, export).
- To request calibration changes, supply at least two independent trustworthy positive/negative or zero reference points, plus the gateway's signed integer scaling spec for each bay. Store credentials, serial identifiers and network addresses only in the private site FAT bundle.
- Note test duration, measured memory/process stability and physical circuit isolation. At 1200 bps with 8E1, approximately 109 bytes/s precedes protocol overhead, so 2,000 SOE may not be practical in a short window. Degraded or skipped test cases must be marked honestly.

## 4. Practical pass criteria for a candidate handoff

- Software gate: exact release SHA, Windows WPF/CI and packaged GUI smoke, CodeQL, native installer update proof, published immutable `-rc.1` prerelease and SHA-256/SBOM present.
- Field gate: at least the independently witnessed F01–F07 observations show consistent event/source identity; F08–F10 explicitly marked `PASS`, `FAIL`, `UNKNOWN` or `NOT TESTED` with authorization record. TI11 Q may remain `counts` pending approved spec without failing the *software display truth* test.
- No production readiness claim until physically verified, including overflow, reconnect, 1200bps and any A/B failover in the actual permitted topology.

References: [SICAM A8000 field diagnostic guide](IEC101_SICAM_A8000_FIELD_FAT.md), [IEC-101 reliability verification matrix](IEC101_RELIABILITY_VERIFICATION_MATRIX.md), [31-point source and calibration boundaries](SCADA_IO_LIST_101_MAPPING_ANALYSIS_20261117.md), [issue #243](https://github.com/masarray/ARIEC60870/issues/243).

## 5. POOP versus POAQ corrected scaling — 18 November 2026

Operator correction: **POOP command TI48 IOA70537 range 0..1000** and **POAQ TI9 IOA790449 feedback display 0..1200**. Prior I/O list and handwritten SCADA page say POAQ 0..1000; preserve these as historical evidence, not proof that the new statement is independently validated. The new bundled reference uses POAQ TwoPoint `X0=0, X100=1, Y0=0, Y100=1200`; command metadata stays 0..1000. Test POAQ against the currently authorized SCADA database and actual received normalized IEC bytes. Do not transmit a POOP command or modify its encoder for this verification. Record approved range revision or **UNKNOWN/NOT TESTED**.
