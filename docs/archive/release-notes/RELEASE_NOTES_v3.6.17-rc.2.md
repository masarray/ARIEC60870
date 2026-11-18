# ArSee v3.6.17-rc.2 — Event FC evidence and SCADA scaling review

**Windows x64 · Public release candidate · 18 November 2026**

This candidate extends [v3.6.17-rc.1](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.17-rc.1), keeping its compact Material toolbar, Excel-like Signal Map, descriptive IEC dropdowns, and shared WPF styles. It does **not** replace the latest stable [v3.6.16](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.16).

## Changes since rc.1

- [#256](https://github.com/masarray/ARIEC60870/pull/256): **Event Log FC** column shows `FC10` (IEC-101 Request user data, Class 1) or `FC11` (Request user data, Class 2) **only with a verified source/link-qualified request–response pair**; unmatched/other-protocol events show `—`. Hover explains that FC11 can carry Class 1 fallback and that FC10/FC11 describes the observed poll, **not a permanent IOA class**. The `RequestFC` evidence is also included in Event CSV.
- [#257](https://github.com/masarray/ARIEC60870/pull/257): reconcile operator-supplied eTerra SCADA range reference, including the later correction **POAQ TI9 measured feedback 0–1200**, distinct from **POOP TI48 outbound setpoint 0–1000**. Default profile retains the 31 CA/IOA/TI points and safe uncalibrated TI11 Q `counts`. Inbound TI9 uses `TwoPoint: X0=0, X100=1, Y0=0, Y100=1200` on **decoded NVA per-unit**, not 32767 raw counts.
- [#258](https://github.com/masarray/ARIEC60870/pull/258): optional **Signal Map → select a signal → More → SCADA reference range for selected signal...**. Deliberately opt-in choices: Q Diri Sendiri signed raw −32768..32767 to engineering ±2078, Q Lab same raw to ±260, or corrected POAQ feedback 0–1200. Only matching CA105/IOA/TI measurement points; shows a confirmation and supports Ctrl+Z. The photographed Q data items were **not mapped to specific FEEDER/KOPEL/TRAFO IOAs**; no bulk Q assignment, no command encoder changes.
- [#259](https://github.com/masarray/ARIEC60870/pull/259): audit fix. **Sync 31 I/O reference** now correctly labels POAQ 0–1200. When an unverified SCADA reference range is first staged, the editor requires **Save As to a new JSON file**, prevents saving to the original station profile, and permits subsequent edits in the new copy. Adds field-isolation/rollback regression tests, including preservation of POOP setpoint.

## Short test checklist

1. For a non-destructive trial use the **portable ZIP** and back up the user-owned mapping JSON. Load the 31-point reference profile; confirm POAQ 790449 (TI9) feedback span 0–1200 and POOP 70537 (TI48) metadata 0–1000.
2. Select **one** Q TI11, choose a Q reference under More, verify X0/X100/Y0/Y100, then test **Ctrl+Z**. Do not accept Q Diri Sendiri versus Q Lab for a particular IOA until eTerra and RTU evidence match.
3. Apply one reference, press **Save** or **Save & Apply**, and verify the app requests a **new JSON filename** instead of overwriting the original. Attempting the original file path must be refused.
4. Open Event Log during a source-qualified IEC-101 unbalanced poll capture: matched FC10 and FC11 responses show their real request FC; missing TX, mismatched source, and IEC-104 show `—`. Verify tooltips and Event CSV export.
5. Confirm search/filter, direct cell edit, Undo/Redo, Mapping/Scaling tabs and shared style have not regressed.

## Engineering limits

The photographed SCADA note suggests `S = √3 × 500 kV × 2000 A ≈ 1732.05 MVA`; 120% ≈ **2078.46 MVA**. The Q Diri Sendiri ±2078 and Q Lab ±260 endpoints remain **reference ranges**, not confirmed per-IOA calibration. A full-scale TI11 signed mapping has a small nonzero output at raw=0 due to asymmetric −32768..32767 limits; do not silently force zero centering. TI32 tap positions and TI13 float units are not inferred from this photo. SCADA endpoint equality requires live RX bytes, source data-item association, known configuration revision and independent FAT proof.

No physical RTU/SCADA, 1200-bps SOE, 125/150% DPI, or command execution FAT is claimed from CI. IEC-101 P0 work [#145](https://github.com/masarray/ARIEC60870/pull/145) remains **unmerged and excluded**. This RC must not be advertised as production IEC conformance or station calibration approval.

## Distribution and provenance

Publish only after the exact-source Package workflow succeeds and verifies portable executable GUI, installer, checksums, SPDX SBOM, and attestations. Official assets:
- `ARIEC60870-v3.6.17-rc.2-setup-win-x64.exe`
- `ARIEC60870-v3.6.17-rc.2-win-x64.zip`
- `SHA256SUMS.txt` and the published SBOM/provenance

The `v3.6.17-rc.2` tag is immutable. Never reuse RC.1 or overwrite its released bytes.
