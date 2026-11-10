# ARIEC60870 v3.6.8-rc.2 — Public Engineering Test Candidate

**Public prerelease; Windows x64; NOT a stable/production release.** This release supersedes rc.1 for public **Frame Trace usability** testing. It is not an IEC 60870 compliance certificate or evidence of physical SICAM interoperability.

## Download and provenance

- **Installer:** `ARIEC60870-v3.6.8-rc.2-setup-win-x64.exe` — per-user Windows Inno Setup; verify its SHA256.
- **Portable:** `ARIEC60870-v3.6.8-rc.2-win-x64.zip` — extract entire ZIP and launch `ARIEC60870.exe`.
- **Integrity:** `SHA256SUMS.txt` and `ARIEC60870-v3.6.8-rc.2-sbom.spdx.json` supplied with release assets. The Package workflow also records build attestations, including the installer, and preserves full per-suite xUnit TRX and real-WPF acceptance screenshots/metrics as workflow artifacts.
- Binaries are **unsigned**. Windows may show an unknown-publisher warning. Check the checksum and source before use.
- rc.1 remains available unmodified. Do not mistake rc.1 installer/ZIP for a build containing these changes.

## Fixes since v3.6.8-rc.1

### P0: Stable Frame Trace observation while reading

- **Fixed WPF flicker/scroll displacement at 1,200/1,200 retained Frame Trace rows under HOLD.** Previously, RX/TX appends and evictions continued mutating the virtualized ListBox even though the engineer had selected HOLD, causing scroll/layout churn.
- HOLD, selected blocks, drag and historical/archive inspection now pin only the **displayed WPF viewport**. The authoritative bounded source ring, protocol acquisition, TX/RX counters, Values, Events and configured capture/history continue receiving independently.
- Displays `+N new (HOLD)` as a lightweight indicator without rewriting the inspected grid.
- Explicit AUTO/Resume synchronizes to the newest retained source frame without collection Reset, preserving original physical byte and identity semantics.
- Regression coverage includes passive GUI smoke, true Windows/WPF 1,400-frame observation and 10,000-frame HOLD/AUTO stress: zero WPF collection changes while held, maintained selected row/scroll position, advancing source, realized AUTO tail, and no live Reset. **These are synthetic CI tests; they are not a physical-field flicker certification.**

### Release verification improvements

- Six xUnit suites write separate release TRX files (no earlier-suite evidence overwrite).
- Installer EXE is independently attested alongside ZIP, checksums and SPDX SBOM.
- The Package workflow now runs passive GUI smoke and the real WPF Frame Trace acceptance against the **exact published Windows executable** before the GitHub Release publishing job. Screenshots and `metrics.json` are downloadable from that workflow run.
- On the precursor `master` source baseline, Windows CI verified in-place upgrade from v3.6.7 with a running GUI and simulated active Master, graceful shutdown, preserved user data, reinstallation and uninstall: [Windows CI #42764404878](https://github.com/masarray/ARIEC60870/actions/runs/42764404878). This is *not* a substitute for independent clean-Windows installation/FAT on the final signed or unsigned rc.2 installer.

### Retained protocol safeguards

IEC-101/103/104 byte evidence, FT1.2 checksum, unbalanced FCB/FCV handling, GI Type/COT validation, SQ=1 IOA overflow guard, malformed ASDU raw-only fallback, TI21 normalized value semantics, IEC-103 Type 2 complete 10-byte DPI/RET/FAN/CP32/SIN decoding, timestamps/quality flags (IV, OV, BL, NT, SB, DST/Valid/Genuine), and unknown/private raw evidence behavior remain unchanged by this presentation fix.

**Important separate development:** Draft [PR #136](https://github.com/masarray/ARIEC60870/pull/136) addresses IEC-101 SICAM 1200-bps serial startup, local write deadline, FC9/Table-10 validation and remote DFC busy recovery. It is intentionally **NOT** included in rc.2 pending supervised device FAT; its passing CI is not physical proof.

## Public test matrix

1. Verify `SHA256SUMS.txt`; install on clean authorized Windows x64 or extract portable ZIP. Record OS/build and any SmartScreen or installer errors.
2. Exercise IEC-101/103 serial or IEC-104 TCP/IP against **isolated test equipment or simulator**. Record which environment was used, baud/parity/stop bits, COT/CA/IOA widths and link address.
3. In Frame Trace, fill 1,200/1,200 frames. Select **HOLD**, scroll to older rows, select frames and leave RX/TX running: visible rows should not flicker, scroll or jump; `+N new (HOLD)` should increase. Select AUTO: newest retained frames should appear. Re-test search scope, source HEX, Events/Values and frame inspector.
4. Test IEC-101 Class 1/2, GI ACTCON/ACTTERM, reconnect, 1200-bps fragmented responses, SOE burst, DFC/ACD, TX echo, CRC/checksum and CP56 time/quality flags. Check **physical response HEX and typed evidence**, not merely an apparent healthy UI status.
5. Try a clean install and upgrade from v3.6.7 while the previous GUI is open; verify saved profiles/captures remain. Use only an authorized isolated bench.
6. Compare raw HEX, Frame Interpreter, Values, Events, GI and exported capture/report. Unsupported/private or malformed types must remain untrusted/raw, not fabricated good values.

## Known limitations / no overclaim

- Long-duration physical SICAM A8000 1200-bps soak, 2/4 independent COM simultaneous capture, high-volume SOE/FIFO overflow, all-vendor IEC-103 variants and real IEC-104 interoperability remain unproven.
- The default live WPF preview is bounded to 1,200 rows; **HOLD is not an unbounded recording guarantee**. Configure and verify full source recording/history when needed.
- RC automation, simulated WPF screenshots and Windows CI cannot certify an actual RTU's wiring, FIFO, clock or protocol implementation.
- Do not use this prerelease to issue controls to an operating substation.

Report reproducible bugs at [GitHub Issues](https://github.com/masarray/ARIEC60870/issues), with **sanitized** TX/RX HEX, protocol settings, version and SHA, steps, expected/actual behavior, and whether real hardware or simulator was used. Never include customer/project credentials, private station topology, or live-system identifiers.
