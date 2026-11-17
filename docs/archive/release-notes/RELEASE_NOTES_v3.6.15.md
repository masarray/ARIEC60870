# ArSee v3.6.15 — Visible IEC-101/104 Scaling in Signal List

**Release candidate notes. Not published until the immutable exact-source Package workflow succeeds.** The current published stable release is [v3.6.14](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.14). The new v3.6.15 assets/URLs must not be advertised until verified.

## What changed

The Signal List Editor previously placed **Engineering scaling mode** after CA, IOA, Type, Name, Group, Type/Role and Unit within one long scrolling `Selected signal` panel. On a normal 1360×820 Windows window, a selected TI9 `TRAFO SOGI TM POAQ` could show only Bay / Description near the end of the panel while all scaling controls were off-screen, despite the scaling engine being correct. This was unusable for routine FAT/SAT calibration.

PR [#245](https://github.com/masarray/ARIEC60870/pull/245) changes the actual WPF editor:

- **Engineering Scaling** and **Mapping & Commands** are separate, compact tabs in the fixed right inspector, not one endless form. A measured analog TI9/10/11/12/13/14/21/34/35/36 opens Scaling automatically; a digital or command row opens Mapping/Commands.
- Scaling offers **Unit**, mutually exclusive **Factor / TwoPoint** mode, and **X0, X100, Y0, Y100** in a compact two-column layout. The full four-input TwoPoint range is directly visible in the default 1360×820 WPF viewport without dragging the scrollbar.
- Factor mode retains Scale/Offset/Ratio primary/secondary and normalized full-scale, independent from the TwoPoint formula. Changing mode never compounds the two calibration mechanisms.
- CA, IOA, TI and Name remain frozen at the left side of the large signal DataGrid while the user scrolls horizontally. Mapping/Commands retains BAY TYPE, business TYPE, MNEMO, state map, class/COT, feedback and command policy.
- Type-aware guidance reminds engineers that TI9 normalized X is decoded per-unit, TI11 scaled X is signed I16 **counts** (not automatically MVar), and TI13 IEEE short float is already in the upstream unit unless project mapping specifies otherwise.

## Why this does not change field scaling accuracy

The prior [v3.6.14](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.14) Factor and TwoPoint implementations remain unchanged. The document-backed 31-point I/O mapping still uses CA=105 and exact IOA/TI identity. The PDF does **not** provide an approved gateway signed integer-to-MVar factor for FEEDER, KOPEL or TRAFO reactive power TI11 points; these remain raw counts until field engineering data is supplied. Track physical bipolar Q calibration in [#243](https://github.com/masarray/ARIEC60870/issues/243).

The editor continues to preserve existing profile JSON, user-owned calibration, original HEX and IEC quality, timestamps, GI/SBO/control identity and all IEC-101/103/104 engine behavior. The new UI does not silently overwrite old profiles; `Sync 31 I/O` still requires explicit review and Save As.

## Real-WPF acceptance

The **exact self-contained Windows executable** runs a UI acceptance probe as part of the existing `--passive-gui-smoke` flow. It opens the actual Signal List Editor at 1360×820, selects TI9 POAQ, and asserts each X0/X100/Y0/Y100 editable field falls within the visible scaling viewport, not merely within the full scrollable content. It also checks TI11 warnings, a digital row's Mapping default, frozen identity columns, and unchanged persisted row data. A first PR candidate failed the above-fold test and was redesigned; the corrected version passed PR software CI.

**Publication acceptance remains a separate gate**: native installed-update FAT on the exact merged commit, Windows/WPF tests, CodeQL, release executable probe, verified Setup/ZIP, checksums, SPDX SBOM, provenance, immutable GitHub tag and Pages documentation update. This release is not an IEC conformance certificate or field SAT.

See [engineering scaling guide](../../IEC10X_ENGINEERING_SCALING.md), [31-point source analysis](../../SCADA_IO_LIST_101_MAPPING_ANALYSIS_20261117.md) and [Release Packaging](../../RELEASE_PACKAGING.md).
