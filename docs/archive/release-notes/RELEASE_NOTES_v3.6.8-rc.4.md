# ArSee / ARIEC60870 v3.6.8-rc.4 — Public Engineering Test Candidate

**Public prerelease · Windows x64 · 11 November 2026.** This is the downloadable Release Candidate for engineers and community testers to exercise ArSee's **normal, fully usable** IEC 60870-5-101/103/104 Master and forensic analyzer workflows. **No artificial application lab mode, isolation wizard, GI/command lock, or extra operating-mode permission gate** has been introduced. Physical test isolation is managed externally by the operator, not by application restrictions.

This is a **test candidate**, not a final stable release, IEC/NUC conformity certificate, certified safety tool, or proof of interoperability with every SICAM A8000 configuration. Please send reproducible feedback.

## Public downloads

Download all assets from **GitHub Releases v3.6.8-rc.4**:

- **Windows installer:** `ARIEC60870-v3.6.8-rc.4-setup-win-x64.exe`. This installs **ArSee** for the current Windows user.
- **Portable Windows app:** `ARIEC60870-v3.6.8-rc.4-win-x64.zip`. Extract the entire ZIP, then start `ARIEC60870.Desktop.exe`. Recommended for a side-by-side public test without upgrading an existing installation.
- **SHA-256 checksums:** `SHA256SUMS.txt` — verify before executing downloads.
- **SPDX software bill of materials:** `ARIEC60870-v3.6.8-rc.4-sbom.spdx.json`.

Unsigned builds may show a Windows SmartScreen unknown-publisher notice. Do not bypass enterprise endpoint policies. Keep a backup of any existing configuration and capture files before testing an installer upgrade; previous-version upgrade/shutdown acceptance is not yet proven on this integrated RC source.

## New in rc.4 compared with rc.3

### More legible IEC-101 Frame Interpreter

- Normal ACD=1 means **Class 1 pending**, shown in **amber**, not faulty-frame red.
- Every structured IOA field has consistent spacing: `IOA3: 128 · IOA2: 0 · IOA1: 107`.
- Child details are clearly attached to the parent signal with `↳ TI-30 Single Point Signal · Quality: ...`.
- Uniform, comfortable WPF interpreter line height and paragraph spacing; preserved compact virtualized rows, original HEX, and true source time/quality bits.
- Raw Frames, Values, Events, GI, Controls, and Report retain direct full-work usability and existing evidence navigation.

### IEC-101 unbalanced single-link and dual-link A/B reliability improvements

- Shared per-physical-link secondary-reply validation and transaction/FCB context. Late, wrong-service, wrong-source or unaddressed short responses are handled conservatively; no invented successful acknowledgment.
- Bounded 1200-bps-aware receive framing, partial-frame expiry and diagnostics for local serial TX timeout/driver input states.
- Fair Class 1 versus Class 2 polling under continual ACD, DFC busy backoff and standby FC9 health supervision.
- Bounded in-memory evidence and explicit loss/eviction accounting. Synthetic 60/500/2,000-event fixtures are included, **not** proof of physical RTU FIFO throughput.
- Configured startup/operator GI works normally. Learning a process ASDU Common Address does **not** silently change an operator's physical control target or cause an unsolicited GI when none was requested. A/B learning and GI policy remain specific to the active physical link.
- Source-qualified dual-link TX and RX evidence, with separate physical A/B ownership and failover GI policy. Historical NUC101 behaviors are project/profile options, not universal forced defaults.

### Validation before release packaging

- Exact combined engine + UI source baseline: `4c3c66def5275d1719077ea857ae3b5ed78b671f`, [PR #145](https://github.com/masarray/ARIEC60870/pull/145) plus merged [PR #153](https://github.com/masarray/ARIEC60870/pull/153).
- [Combined Windows CI #42955419036](https://github.com/masarray/ARIEC60870/actions/runs/42955419036): **952/952 xUnit, 19/19 protocol smoke, Windows GUI and real-WPF acceptance**.
- This RC is built **again** from its own release commit by the published release workflow, which independently requires Windows Release compilation, protocol/xUnit tests, exact-packaged-executable WPF/Frame Trace acceptance, a matching portable ZIP and setup EXE, checksum/SBOM, and build provenance. Do not treat the RC as published until that workflow succeeds and the GitHub Release contains all required assets.

## Known limitations — please help us test

1. **Real Windows serial recoverability** after USB/COM disconnect/reconnect, including in-flight writes, remains under engineering test. No automatic mutating command replay is intended.
2. **Very late, same-service IEC-101 replies** can be intrinsically ambiguous on wire; conservative correlation and explicit uncertain outcomes are being further verified.
3. **GI and select-before-operate lifecycle evidence** (ACTCON, data, ACTTERM, negative confirmations, source/epoch) still needs independent-peer and real-device validation; receiving a link ACK is not proof of complete data coverage or physical actuation.
4. One field capture used **Link Address 105 / configured CA 105** while decoded process data carried **CA 1**. GI to CA105 received ACTCON/ACTTERM but CA-qualified coverage was unverified; GI to CA1 was negatively confirmed. These are diagnostic facts, not proof that CA should be auto-retargeted. Export the raw evidence and confirm actual RTU configuration.
5. **1200 bps SOE and FIFO overflow:** synthetic 60/500/2,000 records pass software fixtures, but real rate/RTU FIFO capacity/driver loss and long-soak accounting require actual field trials. At 1200 bps 8E1, nominal raw line capacity is only about 109 bytes/sec before protocol overhead.
6. The **previous-release installer upgrade / running-Master shutdown** acceptance has not yet received a complete PASS on this integration head. Portable ZIP is recommended for side-by-side trials; clean installer installation is provided for feedback.
7. IEC-101 **balanced active mode**, universal vendor interoperability, all NUC policy variants and production-specific certification are **outside the claims of this prerelease**. IEC-101 passive RX-only capture remains separate from active Master.

## Suggested tester checklist

1. Confirm release tag `v3.6.8-rc.4` in About/asset name and verify the downloaded SHA-256.
2. Check startup, Setup values, hardware serial port, link address, Common Address, COT/IOA widths, 1200 bps 8E1 and user-selected RTS/DTR/handshake settings. Do not infer that Link Address equals Common Address.
3. Connect as a normal active IEC-101 Master with your configured GI/Classes; check exact physical TX/RX, ACD/DFC, GI ACTCON/data/ACTTERM, Class 1/2 fairness and preserved event/HEX evidence. There is **no test-only software mode**.
4. In the Frame Interpreter verify amber ACD, readable `IOA3: ...`, child `↳` detail, stable line spacing, original per-object timestamp/quality and no false success label after a negative GI.
5. In a test setup where permitted, exercise disconnect/reconnect and A/B failover; record whether recoverability and command uncertainty behave correctly.
6. For SOE burst tests, compare RTU-side generated records, original serial frames, application event counts and FIFO/driver overflow indicators. Do not assume a synthetic pass proves 2,000 records survived the wire.
7. Capture the full trace (`.ariec`/trace export), Windows edition, USB-serial chipset/driver, wiring and RTU CPU/firmware details where shareable; report sanitized expected/actual behavior at [GitHub Issues](https://github.com/masarray/ARIEC60870/issues).

If connected to operational equipment, follow site authorization and physical test procedures. **ArSee does not impose additional lab-only gates**. No production control, clock sync or RTU reset is part of these acceptance instructions.

**Engineering milestones and unresolved items:** [IEC-101 reliability tracker #143](https://github.com/masarray/ARIEC60870/issues/143).
