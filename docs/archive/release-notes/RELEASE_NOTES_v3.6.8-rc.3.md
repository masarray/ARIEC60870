# ARIEC60870 v3.6.8-rc.3 — Public Engineering Test Candidate

**Public prerelease · Windows x64 · for supervised engineering evaluation only.** This is an incremental GUI and acceptance-evidence update over `v3.6.8-rc.2`, **not** an IEC conformance certificate, a stable production release, or proof of physical SICAM A8000 interoperability.

## Downloads

- **Windows installer:** `ARIEC60870-v3.6.8-rc.3-setup-win-x64.exe` — per-user Windows Inno Setup.
- **Portable:** `ARIEC60870-v3.6.8-rc.3-win-x64.zip` — fully extract, then launch the application executable.
- **Integrity:** `SHA256SUMS.txt` and `ARIEC60870-v3.6.8-rc.3-sbom.spdx.json`. Verify checksums before running unsigned binaries. Windows SmartScreen may display an unknown-publisher notice.
- Release workflow provides Windows/WPF screenshots, metrics, all six per-suite xUnit TRX files, and build provenance attestations. GitHub Actions artifacts may expire; the four public release assets persist independently.

## What's new since v3.6.8-rc.2

### P1 Setup Config — modern, proportionate toggles

- Nine actual WPF Setup checkbox visuals now use a shared **compact toggle** style: **28 × 16 DIP track, 10 × 10 DIP thumb, 7 DIP caption gap**, 22-DIP minimum row height.
- Toggle tracks and captions are **vertically centered together**, with consistent blue ON, muted OFF, hover, keyboard focus and disabled visual states. No oversized switches, floating label baselines or bulky new setting cards.
- Applies to serial **RTS/DTR**, Reset Remote Link, Reset FCB, Request Class 2 after startup, Clock Sync, General Interrogation, Passive Monitor Link B and optional full-session recording.
- This is **visual-only**: original WPF `CheckBox` control identities, defaults, `IsChecked`, event handlers, saved preferences, keyboard Tab/Space operation, and existing enabled/disabled behavior remain unchanged. It does not change COM handshake, link initialization, RTU commands or wire protocol semantics.

### Windows WPF acceptance — rendered geometry evidence

- The real Windows WPF acceptance now opens active IEC-101 Setup and Passive Monitor Setup, validates visible switches in both views and asserts actual track/thumb dimensions, horizontal caption gap and **switch-versus-text center alignment**.
- Exact executable screenshots: `setup-iec101-compact-switches.png` and `setup-passive-compact-switches.png` in the release workflow's WPF-evidence artifact. These are actual WPF captures, not generated design mockups.
- Existing Frame Trace HOLD/AUTO source-identity and 10,000-frame stress acceptance, 1,400-frame viewport test, protocol smoke and release xUnit regression gate remain enabled. Package publication must fail closed if any required release stage fails.

### Retained fixes from rc.2

- Frame Trace selected-frame HOLD does not mutate the held WPF viewport when new physical TX/RX frames arrive. `+N new (HOLD)` and deliberate AUTO resume allow engineers to inspect without flicker or losing original raw evidence.
- Normal protocol evidence, IEC-101/103/104 master and passive workflows, bounded acquisition/trace distinctions and established decoder safeguards remain as in rc.2.

## Excluded ongoing work and test boundaries

- **Draft [PR #136](https://github.com/masarray/ARIEC60870/pull/136) — IEC-101 serial transport and SICAM startup/FC9/DFC/FCB diagnostics — is NOT merged into this prerelease.** CI and simulator success do not prove that a CP8050/CI8551 on real 1200-bps serial wiring reliably replies.
- A field case captured ARIEC FC9 TX without complete RX frames, while a separate IEC_Test trial on its configured COM port received link/class data. Do not attribute that to COT byte widths or an IEC violation without identical port/handshake verification, raw UART RX evidence and supervised FAT.
- IEC-101 active balanced mode, universal vendor conformance, uninterrupted high-volume SOE/FIFO, 2/4 physical RX taps and production control safety have **not** been field certified.
- The in-memory Frame Trace preview and HOLD are not substitutes for a deliberately configured archival recording. Never claim a displayed no-data response proves every signal has been captured.
- Do **not** use this prerelease to issue controls on production substations or reset a live RTU. Use authorized isolated test equipment.

## Public verification checklist

1. Verify the installer/portable SHA256 and note version `v3.6.8-rc.3`, Windows edition and display scaling.
2. Open **Setup Config** in IEC-101 and IEC-101 Passive Monitor modes. Verify all visible toggles are proportionate, visually aligned, operable via mouse and Tab/Space, disabled when appropriate, and persist after re-opening setup; do not accidentally change RTS/DTR on an operational station.
3. Exercise Frame Trace HOLD/AUTO with >1,200 received frames and the original selected frame, scroll and evidence HEX visible. Confirm no flicker, selection jump or unexplained Reset.
4. Against a simulator or isolated authorized RTU, compare typed link/ASDU evidence with raw TX/RX; record framing/parity, port, link and CA/COT/IOA widths, ACD/DFC, GI, Class 1/2, malformed/unknown frames.
5. Test clean installation and upgrade only with backups of profile and capture files. Verify program launches and previous user files persist.
6. Report sanitized findings at https://github.com/masarray/ARIEC60870/issues with screenshots, reproducible steps, expected/actual behavior, version and test environment. Do not publish customer serial-number, credentials, private substation topology or raw sensitive site data.

**Publication source:** derived from `master` after merged [PR #140](https://github.com/masarray/ARIEC60870/pull/140) and [PR #141](https://github.com/masarray/ARIEC60870/pull/141), with `master` UI checkpoint `528d4e6b15e94b948fa22289f0004778e9075f75`. The release-branch-only commit adds this release note; this note is not a new protocol implementation.
