# ArSee v3.6.17-rc.3 — Verified Values scaling and unmapped IOA diagnosis

**Windows x64 · prerelease test candidate · 18 November 2026**

Built from merged [PR #262](https://github.com/masarray/ARIEC60870/pull/262) on `master@5503f1ac8fefe177775217a2f800dd32a2bcab92`, plus this branch's exact RC.3 version stamp and release notes. This release supersedes [v3.6.17-rc.2](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.17-rc.2) for **evaluation**, without modifying that immutable release. The last stable v3.6.16 remains unchanged.

## Reproduced screenshot issues and software corrections

- **POAQ feedback** at CA105, TI9, IOA790449: the operator-corrected bundled reference is `TwoPoint X0=0, X100=1, Y0=0, Y100=1200 MW` (separate from **POOP TI48 outbound command 0..1000**). Incoming normalized X=32767/32768 projects to **1199.96337891 MW**, retaining any QDS `OV` quality indication. Previously the Values fallback could display preformatted `0.999969482422 MW` even when this mapping was not successfully applied.
- **Q** at CA105 TI11 IOA790447 (FEEDER), IOA790439 (KOPEL), IOA790443 (TRAFO): bundled profile remains **uncalibrated `counts`**. An incoming signed SVA=13641 projects to `13641 counts`, **not `13641 MVar`** unless an explicit profile-approved Q TwoPoint is selected. The SCADA screenshot contains two different Q ranges, **±2078** and **±260**, but does not bind either range to the three specific IOAs. Blind default calibration would assert invented MVar measurements.
- **Unmapped received IOAs** such as `790349`, `1790446`, `1790447` are **not present in the supplied 31-point CA105 Signal Map**. Values now uses exact physical RX identity and a one-time diagnostic instead of letting an upstream guessed signal name or mapped flag hide the discrepancy. **No automatic IOA spelling/digit correction**; check original HEX against the simulator/gateway database before editing a copied JSON.
- A single source-qualified **measurement projection** now serves both live Values and cached Save & Apply reprojection. Only a verified canonical IEC source object and a strict CA/IOA/TI-family point can yield engineering output. Missing source proof visibly says `[scaling not verified]` and cannot silently append a guessed MW/MVar unit.
- Values compact header now shows the count of active Signal Map references alongside received points. It is **not** a claim that all expected points were received or that every IOA has a certified calibration.

Regression tests reproduce the TI9 source value and quality flag, signed TI11 Q, cached ValueRow profile reproject, the missing-source fallback, and all three extra original IOAs. Windows Release/xUnit/protocol-smoke and CodeQL on source PR #262 passed.

## Read-only acceptance checklist (virtual inline VSPE or authorized isolated RTU)

1. Save a copy of the station's own Signal Map JSON; do not overwrite an existing site-approved profile. Start with a read-only monitor of actual traffic, not an unsolicited SCADA command.
2. Confirm active mapping reference count, then compare Values against **Signal Map**. For incoming CA105 IOA790449 TI9 X≈0.999969, expect ≈1199.963 MW only if that reference profile is the active strict match, and separately see `OV` if wire QDS has OV.
3. For Q with the uncalibrated default, expect signed raw integer **counts**, not unproven MVar. The existing `More → SCADA reference range for selected signal...` allows explicit single-point ±2078 or ±260 candidate previews and Ctrl+Z; **Save As** requires a distinct file. Obtain actual eTerra/RTU data-item-to-IOA association before using MVar.
4. Confirm incoming `790349`, `1790446`, `1790447` appear **unmapped** with exact CA/IOA/TI/source diagnostics rather than being silently translated to `790439`, `790446`, `790447`. Only change a saved project map if verified against original HEX and RTU/simulator source configuration.
5. For tap IOA790448, the screenshot observed untimed TI5 and value -4 while the original mapping lists TI32/1..19; this is a **pending source specification conflict**. Never invent an offset (+5), convert TI5 into TI32, or mistake historical profile limits for physical travel proof.
6. Re-check Event Log/Trace/Values original frame parity, quality, Ctrl+Z, Save & Apply and WPF rendering at 100/125/150% DPI. Record actual evidence or mark `NOT TESTED`.

## Proof boundaries and distribution

Package publication is allowed **only after** its exact-source GitHub Actions workflow passes Windows xUnit, smoke, packaged EXE/WPF, installer build, ZIP structure, SHA-256, SPDX SBOM and artifact attestations. Publishing this notes file does not alone establish package success.

Neither the source CI nor Windows Package can establish **real** SICAM A8000 timing/reconnect, RTU FIFO 60/500/2,000 event parity, the source-specific Q MVar calibration, multi-vendor interoperability or command execution/physical switchgear travel. Draft IEC101 core #145 remains separate.

On successful publication, release assets will be named:
- `ARIEC60870-v3.6.17-rc.3-setup-win-x64.exe`
- `ARIEC60870-v3.6.17-rc.3-win-x64.zip`
- `ARIEC60870-v3.6.17-rc.3-sbom.spdx.json` and `SHA256SUMS.txt`

Do **not** reuse or mutate the public `v3.6.17-rc.1` / `v3.6.17-rc.2` tag or binaries.
