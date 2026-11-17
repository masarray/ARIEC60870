# ArSee v3.6.16-rc.1 — IEC-101/104 physical FAT candidate

**Classification:** public pre-release for *authorized, isolated field-equivalent testing*, not a stable release, IEC conformance certificate, or proof of correct RTU/gateway engineering calibration.

**Source branch:** `release/rc-3.6.16-rc.1` (immutable candidate tag will point to its exact source commit). **Published stable fallback:** [v3.6.15](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.15).

## Candidate features

- Includes merged Signal Map workbench #248: visible Ctrl+F search, bay/kind filters, focused eight-column grid, advanced columns in More, easy BAY TYPE + TYPE + MNEMO (+ #) generation and Scaling-first analog inspector.
- Adds **More → Export blank Field FAT worksheet...** to the same editor. This is a read-only CSV derived from the current validated 31-point or custom user-owned Signal List, even when the user has unsaved edits. Excel-compatible UTF-8 BOM and quoted cells; textual formula injections are prefixed; source-specific connection passwords/hosts are not exported.
- The worksheet contains effective CA/IOA/TI, signal name, bay/business TYPE/mnemonic, the **active** Factor *or* TwoPoint settings, and blank fields for independent observation time, decoded raw value, engineering display, independent RTU/SCADA reference, COT, QDS, source, original HEX/frame reference, calibration authority and field engineer notes. Every result is **NOT TESTED** until a human records physical proof. Dormant settings are intentionally not shown as active calibration.
- Unverified TI11 reactive power counts remain **counts**, even when the bundled I/O source documents CT 2000/1 A and PT 500/0.1 kV. No guessed MVar, no implicit IEC unit conversion, no command permission changes, no native engine/FCB/GI timing modifications.
- Includes a complete [RC field execution and evidence matrix](../../FIELD_FAT_RC_3_6_16.md) for the SICAM A8000 and other independently documented IEC-101/104 RTU profiles.

## Software gates required for release

- Exact-head Windows build and all regression suites, 19 protocol smoke vectors, formatter and repository hygiene.
- Real WPF packaged-executable GUI smoke (including Scaling above-fold and Signal Map workbench checks), 1400-frame OBS-C acceptance.
- Native Windows installed-update FAT from previous published Setup plus CodeQL/dependency checks on PR/source; Package creates checksum, SPDX SBOM and provenance.
- A **published prerelease**, not a draft, with installer ZIP, portable ZIP, checksum and SBOM, with tag SHA identical to the accepted release-branch commit. Never overwrite an existing tag or change released binary bytes.

## Physical testing — still outstanding

This release does **not** establish any of the following by software testing alone:
- Physical COM/RS232/RS485 wiring, modem control RTS/CTS, SICAM firmware behavior, actual LA/COT/CA/IOA interoperation.
- The true signed TI11 Q counts-to-MVar calibration for FEEDER IOA 790447, KOPEL 790439 and TRAFO 790443 ([issue #243](https://github.com/masarray/ARIEC60870/issues/243)).
- Hardware SOE/FIFO integrity during 60/500/2000 changes at 1200 bps; a test batch may physically exceed available throughput.
- GI expected point completeness, command/SBO actuation, dual-circuit failover and sustained production-grade interoperability.

Use the field matrix to record observed stages and attach sanitized original HEX/verified capture. **Do not operate breakers, modify clock, send setpoints, reset a production link or disconnect a live SCADA master to execute a software checklist.** The on-site engineer must separately authorize and isolate any active Master tests.
