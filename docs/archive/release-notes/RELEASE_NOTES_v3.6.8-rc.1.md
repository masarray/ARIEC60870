# ARIEC60870 v3.6.8-rc.1 — Public Engineering Test Candidate

**Pre-release / public evaluation only — Windows x64.** This is a community test candidate, not IEC conformance certification, a production SCADA approval, or a claim that every IEC-101/103/104 device and Type ID is supported.

## Download

- **Recommended for most testers:** `ARIEC60870-v3.6.8-rc.1-setup-win-x64.exe` — per-user Windows Inno Setup installer (no administrator access required in normal per-user installation).
- **Portable / non-install:** `ARIEC60870-v3.6.8-rc.1-win-x64.zip` — extract the complete ZIP and run `ARIEC60870.exe`.
- Also included: `SHA256SUMS.txt` and SPDX dependency SBOM. Verify SHA-256 before executing the package.
- Windows may show an unknown-publisher warning because these are **unsigned RC binaries**. Do not ignore unrelated or unexpected security warnings; verify the checksum from this release page.

## What's available to test

- IEC-101 and IEC-103 active master sessions, IEC-104 TCP active master, and a separate receive-only IEC-101 observation workflow with original physical frame evidence.
- Engineer Mode Frame Trace: monospace raw HEX **and** decoded interpreter, adaptive width when Frame Details opens, structured decimal IOA `IOA3–IOA2–IOA1`, exact original wire bytes in physical order `IOA1–IOA2–IOA3`.
- Source-backed device timestamps with CP56 Valid/Genuine/DST where actually encoded; active quality flags including `Overflow (OV): TRUE`, `Invalid (IV): TRUE`, ACD and DFC.
- IEC-101 dual-link diagnostics and verified link-address handling; values, event ledger, replay, findings, and export/report for supported trusted data.
- Decoder safeguards for malformed ASDU headers, SQ=1 IOA overflow, GI termination identity, TI21 NVA without QDS, and **IEC-103 Type 2 complete 10-octet event structure** with correct RET/FAN/CP32/SIN offsets. Unsupported/private or partial data should remain raw evidence, not an asserted good process value.

## Scope and known limitations

- **IEC-101 active balanced mode is not established as supported.** Dual-link unbalanced handling is not proof of balanced-mode operation.
- **IEC-103 vendor/private Type IDs and uncommon element variants are not universally decoded**; use the original bytes and the device's interoperability table. A decoded field is authoritative only where complete and valid.
- The separate receive-only IEC-101 capture setup does **not** prove electrical isolation of a physical tap.
- Long duration 1200-bps serial load, 2/4-COM simultaneous RX, SICAM high-volume SOE FIFO handling, real multi-vendor IEC-103/104 reconnect, and field GI/command feedback still require supervised test evidence.
- The compiled RC installer is supplied for public **clean-install and upgrade testing**. Automatic upgrade from v3.6.7 and Windows Restart Manager behavior must not be assumed proven unless a completed matching acceptance run explicitly reports success. Back up exported captures/profiles before testing upgrades.
- Never use this candidate to issue controls to a production substation. Work only on authorized isolated benches. A GI ACTTERM proves a protocol response, **not** correctness of every expected point or mechanical actuation.

## First test in five steps

1. Verify `SHA256SUMS.txt` and install or extract on Windows x64.
2. Use a virtual paired COM setup with an authorized IEC-101 slave simulator, or a test IEC-104/IEC-103 endpoint.
3. In Setup, match the actual serial speed/parity, link address, COT/CA/IOA widths, and device profile; connect to **test equipment only**.
4. Exercise normal frames, noise/fragmentation, GI, Class 1/2, events, timestamp validity, quality flags, and Frame Details open/close. Verify that RX HEX bytes match the interpreter, values, journal, replay, and report.
5. File reproducible public defects at https://github.com/masarray/ARIEC60870/issues using sanitized HEX, config widths, expected versus actual, app version, simulator/device model and protocol. Remove IPs, customer names, credentials and private plant data.

Automated Windows builds/xUnit/WPF checks indicate source regression coverage, **not physical FAT**. This candidate is intentionally labeled prerelease while those independent tests are collected.
