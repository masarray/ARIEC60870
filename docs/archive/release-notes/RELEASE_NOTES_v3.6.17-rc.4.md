# ArSee v3.6.17-rc.4 — SCADA Q engineering ranges by bay

**Windows x64 · prerelease test candidate · 19 November 2026**

This candidate adds the explicit SCADA data-item/bay association received from the operator on 19 November to the already merged and tested [PR #263](https://github.com/masarray/ARIEC60870/pull/263), based on `master@b13f47117762f8520e9bf8eefaf54b6daa222101`. The older [v3.6.17-rc.3](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.17-rc.3) remains immutable. This is not a stable promotion or RTU field FAT certification.

## Source-specific Q scaling now visible in the bundled 31-point Signal Map

The older `scada.sdb` photograph provided two Q engineering ranges but did not identify which bay used each. The **later operator clarification** resolves that gap for this particular reference:

| Operator's bay | SCADA data item | CA | IOA | IEC Type | Decoded IEC signed source | Engineering display range |
| --- | --- | ---: | ---: | ---: | --- | --- |
| Feeder | **Q Diri Sendiri** | 105 | 790447 | TI11 | SVA I16 **−32768..32767** | **−2078..+2078 MVar** |
| Kopel — Lab SOGI 3 | **Q Lab** | 105 | 790439 | TI11 | SVA I16 **−32768..32767** | **−260..+260 MVar** |
| Trafo — Lab SOGI 4 | **Q Lab** | 105 | 790443 | TI11 | SVA I16 **−32768..32767** | **−260..+260 MVar** |

Each bundled reference Q now contains `scalingMode=TwoPoint`, all four endpoints, `unit=MVar`, and the corresponding engineering min/max. The same JSON is used for the Windows packaged seed and human-readable docs mirror. This is a **source-reported engineering display map**, not evidence that every field gateway's raw IEC SVA matches SCADA's internal raw domain. Compare with actual signed RX and independent SCADA measurement before accepting site calibration.

At raw signed X=0, the *literal* asymmetric `−32768..32767` conversion yields approximately **+0.0317 MVar Feeder** and **+0.00397 MVar Lab**; the firmware does not silently clamp/alter the original source value or IEC QDS to report an attractive zero. Calibration must be checked at zero and both sign directions before approving.

## Safe upgrade of an existing locally saved Signal Map

An installer **does not overwrite** a saved project JSON or silently remap its Q readings. Open Signal Map / **Sync 31 I/O** to preview matching CA=105 / exact IOA / TI11 rows; only a truly **legacy, uncalibrated Factor identity row** with no custom endpoints or min/max is eligible for its corresponding Feeder/Lab SOGI 3/4 Q TwoPoint reference. Any custom Factor or TwoPoint mapping, different station/Type, command config, point identity, feedback wiring, or raw record is preserved. Accept the preview and **Save As a NEW JSON profile**; that new copy must then be applied to the active monitoring session.

Manual per-signal SCADA reference range preview remains available with explicit confirmation, including a warning when choosing an alternative to the latest operator association; nothing is imposed on user-owned profiles.

## Unchanged source semantics / continued RC.3 fixes

- **POAQ inbound TI9 normalized feedback IOA790449** remains **0..1200** MW, and distinct **POOP outbound TI48 setpoint command IOA70537** remains **0..1000** command metadata. No setpoint encoder changes, physical command TX or new command privileges.
- Original CA/IOA/TI, IEC ASDU raw HEX, QDS OV/IV, timestamps, GI/SBO and Event Log forensic evidence are unchanged.
- Unknown extra received IOAs such as 790349, 1790446, 1790447 continue to show unmapped exact physical identities, not guessed aliases.
- TI13 P source float identity and TI5/TI32 step-position family remain as before. Historical tap range **−4..14** versus **1..19** requires separate station evidence and is not inferred here.
- Separate old documentation-only **±1732 all-bay nominal CT/PT hypothesis** is marked *superseded for this source*; it must not be mistaken for an approved feeder or lab mapping.

## Software qualification versus physical FAT

The source PR #263 passed Windows CI, **1,249 xUnit** (0 failures), 19/19 protocol smoke, Signal Map real WPF acceptance, repository hygiene, dependency review, and CodeQL. **This source-level qualification is not package certification.** Before publishing the public release, the **exact RC.4 package build** must pass full xUnit, WPF packaged executable smoke, setup EXE, portable ZIP, SHA-256, SBOM and artifact attestation.

Physical IEC101/104 FAT on SICAM/other third-party devices, actual 1200-bps SOE completeness, Lab Q calibration at negative/zero/positive samples, and independent RTU-to-SCADA engineering reference are **NOT TESTED** by this report. Do not promote to stable or claim field approval based on synthetic tests alone.

Expected artifacts after a successful source-bound public workflow:
- `ARIEC60870-v3.6.17-rc.4-setup-win-x64.exe`
- `ARIEC60870-v3.6.17-rc.4-win-x64.zip`
- `ARIEC60870-v3.6.17-rc.4-sbom.spdx.json`
- `SHA256SUMS.txt`

Earlier RC.3 package stays available without modification. Choose the portable ZIP first for isolated read-only evaluation.
