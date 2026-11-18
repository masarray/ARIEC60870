# ArSee v3.6.16 — Signal Map workbench and Windows engineering test release

**Windows x64 · Source-qualified stable release preparation · 18 November 2026.**

Publication is **not** established by this notes file. Official Windows Setup/portable ZIP, SHA-256, SBOM, provenance and an immutable `v3.6.16` tag must first pass the exact-source Package workflow. Until then, [v3.6.15](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.15) remains the previously verified public stable version.

## Signal Map: grid-first engineering workflow

Changes since v3.6.15 on the current `master` source:

- [#248](https://github.com/masarray/ARIEC60870/pull/248): find by exact CA/IOA, signal name, bay, TYPE or mnemonic; combine bay/kind filters; an eight-column daily grid with optional advanced columns; explicit BAY TYPE + TYPE + MNEMO (+ existing number) name composition. Exact numeric IOA search prefers the primary point over command feedback references.
- [#250](https://github.com/masarray/ARIEC60870/pull/250): Excel-style direct cell edit, copy/paste TSV, multi-cell selection, Fill Down and Undo/Redo. Pasted batches are validated against a staged profile and rejected atomically for malformed, out-of-range or invalid IEC data. Esc restores native WPF cell edits.
- [#251](https://github.com/masarray/ARIEC60870/pull/251): grid-first layout. A compact header/search/action row gives the DataGrid maximum working area; the resizable Details inspector opens on demand with `Ctrl+I` instead of permanently narrowing the grid.
- [#252](https://github.com/masarray/ARIEC60870/pull/252): toolbar Material vector icons and exactly the same action-pill, primary-pill, icon-only and search-shell WPF style resources as Main Window.
- [#253](https://github.com/masarray/ARIEC60870/pull/253): Scaling/Mapping use the same shared toggle-pill styling and Material icons, with consistent field spacing, centered content and compact inspector controls.

For analog TI9/10/11/12/13/14/21/34/35/36, the Scaling page continues to expose Unit and either Factor or TwoPoint; X0/X100/Y0/Y100 remain above the fold in tested CI viewport. Digital/command points open Mapping. User-owned JSON profiles, command policy, IEC source identity, raw frame/quality evidence and Save & Apply semantics remain unchanged.

## Field evidence worksheet (included in source)

**Signal Map → More → Export blank Field FAT worksheet...** creates an Excel-compatible UTF-8 CSV from the validated current Signal List. It records mapped identity and active scaling parameters while leaving independent field observations, calibration authority and evidence references blank with result **NOT TESTED**. Export is not a calibration approval or a command action. Reference [field FAT matrix](../../FIELD_FAT_RC_3_6_16.md).

## Evidence and safety boundaries

The merged GUI PRs passed Windows Release compilation, formatting, protocol smoke, xUnit, native WPF screen/interaction tests, CodeQL and repository hygiene on their reviewed heads. This release additionally requires exact **merged master** Windows CI, self-contained executable GUI probe, independent installed-updater FAT, Package asset verification, SHA-256 checksums, SBOM and provenance. Source-PR green checks alone do not satisfy the release workflow.

**No physical IEC-101/104 RTU or SCADA FAT is claimed.** Signed TI11 Q counts must not be displayed as MVar without independently approved RTU scaling (see [#243](https://github.com/masarray/ARIEC60870/issues/243)); a 1200-bps SOE stress test requires physical FIFO/wire evidence. Open IEC-101 reliability work in [#145](https://github.com/masarray/ARIEC60870/pull/145) is **not merged and not included** in this release. Never perform active command/GI, serial-control or clock changes on a production link without site authorization.

## Distribution

When the Package workflow succeeds, use its official `v3.6.16` GitHub Release assets:

- `ARIEC60870-v3.6.16-setup-win-x64.exe` — installer for managed Windows use.
- `ARIEC60870-v3.6.16-win-x64.zip` — self-contained portable Windows application.
- `SHA256SUMS.txt`, SPDX SBOM and published build provenance.

Do not reuse an older v3.6.16 tag for different source bytes or advertise those download paths before publication succeeds.
