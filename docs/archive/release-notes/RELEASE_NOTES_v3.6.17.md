# ArSee v3.6.17 — Public stable Windows software release

**Windows x64 · Published public stable software release · 19 November 2026**

**v3.6.17 is published public stable**, not a draft or prerelease: [official GitHub Release](https://github.com/masarray/arsee/releases/tag/v3.6.17), immutable source `5b1eada52cb26295dd91d73afe3b9d065e71a404`, exact-source [Windows CI and native independent installed-updater FAT #44539045530](https://github.com/masarray/arsee/actions/runs/44539045530), plus successful [Package #44540333243](https://github.com/masarray/arsee/actions/runs/44540333243) with installer, ZIP, checksums, SPDX SBOM and provenance. The previous stable [v3.6.16](https://github.com/masarray/arsee/releases/tag/v3.6.16) and the archived prerelease [v3.6.17-rc.7](https://github.com/masarray/arsee/releases/tag/v3.6.17-rc.7) remain available.

## Operator workflow improvements since v3.6.16

- **Events / FC10–FC11:** concise “Class 1 Data Polling” / “Class 2 Data Polling” hints. The label applies only to observed, qualified request/response polling and does not claim the IEC class of any IOA. Original TX/RX evidence and CSV `RequestFC` remain authoritative.
- **Latest Event:** its highlighted state follows *actual auto-scroll/follow* behavior, including user scroll and selection changes. It does not change event acquisition, ordering or retention.
- **Simpler tooltips:** frequently used actions have short, clear labels; source-backed IOA/address/quality/forensic details remain intact rather than being truncated. The short-tooltip policy is maintained in `AGENTS.md`.
- **Bilingual IEC handbook / Help Center:** unified English/Indonesian learning and Signal Map guidance, concise operator-oriented workflow, and Help WPF acceptance. Real OS DPI operator qualification at 125%/150% is still separate.
- **Signal Map:** grid-first editor, filter/find, atomic multi-cell editing, Fill Down, Undo/Redo, opt-in Scaling/Mapping inspector, explicit Save/Apply. Source identity/quality and project-owned mapping remain unchanged.
- **Station reference safeguards:** the 31-point demonstration profile and optional Q reference previews distinguish TI11 signed raw counts from unapproved MVar scaling; separately keep POAQ TI9 feedback 0–1200 and POOP TI48 outbound command metadata 0–1000. No universal station scaling is asserted.
- **Website:** bilingual GitHub Pages landing, guided demo, FAQ and download pages were synchronized with the latest published RC, with the last stable channel clearly labeled. Post-publication Pages metadata must be updated to reference the successful stable v3.6.17 assets.

## Qualification and scope boundaries

This public **software stability** designation denotes the published reproducible Windows installer/ZIP and source-bound software tests; it is **not IEC conformance certification or physical RTU/SCADA acceptance**.

The published `v3.6.17` release pipeline demonstrated:
1. Exact merged source version 3.6.17, Windows Release build, C# format gate, protocol smoke, full xUnit and GUI/WPF evidence tests.
2. Independent genuine installed-updater/previous-release FAT on the **same exact master commit**, not an older RC or synthetic-only test.
3. Release Package verification of the self-contained application, installer, portable ZIP, checksums, SPDX SBOM and provenance; no tag/asset overwrite.

**Physical field FAT remains NOT TESTED:** actual SICAM A8000/other IED interoperability, real 1200-bps SOE/FIFO and failover, independent TI11 Q counts-to-MVar calibration, physical 100%/125%/150% Windows DPI acceptance, and authorized GI/SBO command behavior. Keep any customer station topology and captures private. Do not operate a second active Master on production lines.

See [v3.6.17 RC.7 scope](https://github.com/masarray/arsee/releases/tag/v3.6.17-rc.7), [field FAT worksheet](../../FIELD_FAT_RC_3_6_17_RC2.md), and [IEC field implementation draft #145](https://github.com/masarray/arsee/pull/145) (**unmerged**). Do not claim pending #145 changes are in the product.

## Official distribution

The release Package published and verified these exact assets:
- `ARIEC60870-v3.6.17-setup-win-x64.exe` — per-user Windows x64 Inno Setup installer.
- `ARIEC60870-v3.6.17-win-x64.zip` — self-contained portable Windows x64.
- `ARIEC60870-v3.6.17-sbom.spdx.json` — dependency inventory.
- `SHA256SUMS.txt` — integrity checks.

The installer is unsigned; compare the published SHA-256, follow organizational software installation policies, and use only authorized/isolated test equipment.
