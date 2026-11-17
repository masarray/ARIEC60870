# ArSee v3.6.14 — IEC-101/104 engineering scaling and source-grounded Signal Mapping

**Release candidate notes — not a statement of publication.** This document is bound to the source version `3.6.14` in `Directory.Build.props`. Public links, actual tag/commit, installer SHA-256 and provenance must be filled from a successfully completed immutable Package workflow. Until then, the latest published stable remains [v3.6.13](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.13).

## 1. Engineer-configurable analog scaling

- IEC-101/104 measured values may be normalized (**TI9/10/21/34**), signed scaled (**TI11/12/35**), or IEEE-754 short float (**TI13/14/36**). The IEC type identifies encoding, **not** an RTU-specific engineering-unit factor.
- Signal List provides mutually exclusive **Factor** and **TwoPoint** projections. Factor uses `engineering = source × normalized-full-scale(if NVA) × scale × primary/secondary + offset`. TwoPoint uses `Y = Y0 + (X−X0) × (Y100−Y0)/(X100−X0)`, where X is the **decoded** source value (NVA in per-unit, SVA signed integer, IEEE float as sent).
- Both negative and positive ranges are supported. Invalid/nonfinite endpoints or zero X span are refused. TwoPoint does not additionally multiply by Factor, CT/PT or NVA full-scale. Off-range values are extrapolated rather than silently clamped; never interpret extrapolation as RTU certification.
- Original capture bytes, IEC quality, CA/IOA, timestamps, controls and GI evidence are preserved. The engineering projection appears in Values, Event Log and its history/CSV and Frame Trace when the exact point identity and trusted measurement are available.
- **TI13 P** is identity when the upstream device already sends MW. **TI11 Q** requires the authorized gateway `signed counts → MVar` conversion. A raw observation such as 13641 **must not** appear as 13641 MVar merely because a unit field says MVar.

## 2. SCADA I/O list — 31 exact point names

Based on the operator-provided **I/O LIST PENGUJIAN SCADA** (31 rows, page 1) previously reconciled in [#240](https://github.com/masarray/ARIEC60870/pull/240), the bundled example uses `BAY TYPE + TYPE + MNEMO + LAB (when present)`, preserving CA=105, original IOAs, IEC TI, command policy and feedback:
- `FEEDER TM P` — IOA 790446, **TI13**; `FEEDER TM Q` — IOA 790447, **TI11**.
- `KOPEL TM P 1` / `KOPEL TM Q 1` — IOA 790438/790439, **TI13/TI11**.
- `TRAFO TM P 2` / `TRAFO TM Q 2` — IOA 790442/790443, **TI13/TI11**.
- `TRAFO SOGI TM POAQ` — IOA 790449, **TI9**, source-documented *real power setting (0/1000)*; X0=0, X100=1 NVA, Y0=0, Y100=1000 engineering.
- Existing digital indication, remote command, and command-feedback identities remain source-aligned, with no guessed station alias for an unrelated CA or Type ID.

CT 2000/1 A and PT 500/0.1 kV from the I/O list provide a theoretical apparent-power nominal magnitude (~1732.05 MVA) but **do not** establish the RTU TI11 signed-count scaling to MVar. The bundled default deliberately labels those three uncalibrated reactive-power readings `counts`. The separate [provisional scaling example](../../SCADA_IO_LIST_101_MAPPING_ANALYSIS_20261117.md) is **not a default or FAT-approved calibration**.

## 3. Upgrade existing Signal Lists safely

Already-saved local user Signal Lists are never replaced by installation. For an older project mapping, open **Signal List → Sync 31 I/O**, check the preview of exact CA+IOA+TI matches, and confirm **only if those points belong to the intended project**. Save & Apply to a **new JSON filename**. This updates names and business metadata, replaces an uncalibrated legacy TI11 Q label `MVar` with `counts`, and applies the documented TI9 POAQ mapping *only* to legacy identity/unconfigured rows.

The action keeps custom Factor/TwoPoint calibration, command policies, feedback IOAs, station identity, communications, and other stations unchanged. The editor also now retains business type and optional engineering min/max metadata on save/duplicate. No user profile is silently migrated at startup.

To display actual MVar, have the approved RTU/gateway **factor or two independent endpoint pairs** and then enter them in the selected Q point. The software must not infer a usable calibration from one observed `1730 MVar` reference.

## 4. Software release acceptance and boundaries

The source-milestone PRs [#239](https://github.com/masarray/ARIEC60870/pull/239), [#240](https://github.com/masarray/ARIEC60870/pull/240) and [#241](https://github.com/masarray/ARIEC60870/pull/241) have been merged. Their software regression work and the prior merge commit's exact-source CI/CodeQL/hygiene were green **before preparing this version**. These results are **not** qualification for a future `v3.6.14` release binary.

A public stable release requires all gates from [Release Packaging](../../RELEASE_PACKAGING.md): exact-source Windows build, protocol and all xUnit suites, real-WPF acceptance of the packaged executable, **independent genuine previous-release installed-updater FAT**, immutable package verification, SHA-256 sums, SPDX SBOM and provenance. Physical RTU/SCADA signal scaling, serial 1200-bps 2,000-event SOE parity, and multi-vendor controls/GI tests remain separate field acceptance. The binaries are currently unsigned; nothing here claims IEC conformance certification.

**Sources:** [engineering scaling guide](../../IEC10X_ENGINEERING_SCALING.md), [document-backed mapping analysis](../../SCADA_IO_LIST_101_MAPPING_ANALYSIS_20261117.md), and the immutable [v3.6.13 stable release](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.13).
