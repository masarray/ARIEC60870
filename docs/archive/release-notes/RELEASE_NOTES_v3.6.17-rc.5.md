# ArSee v3.6.17-rc.5 — Bilingual Help Center R4.1 and reconciled engineering build

**Public Windows x64 prerelease / evaluation candidate — 19 November 2026.**

This release candidate builds on the **reconciled `master` after [PR #265](https://github.com/masarray/arsee/pull/265)**, including subsequent Signal Map ownership, display, IEC101 DFC-busy, and canonical repository/website fixes. It supersedes [RC.4](https://github.com/masarray/arsee/releases/tag/v3.6.17-rc.4) for testing; previously released artifacts are left unchanged.

## What changed

- **Native bilingual Help Center (English / Bahasa Indonesia)**: compact article-first WPF design, restrained iconography, topic search/reset, responsive topic/sidebar reading area, keyboard navigation, and contextual F1. The Signal Map editor now opens its detailed guide directly via F1 or its More action.
- **Shared offline engineering handbook and online Field Wiki**: the same canonical bilingual corpus is bundled under `Learning/` in the Windows application and served on GitHub Pages at `https://masarray.github.io/arsee/`. Contains **28 chapters, 128 sections, 58 operational workflows and 20 guided IEC trace labs**; 13 Signal Map lessons explain CA/IOA/TI, scaling, QDS, states, profile validation, Save/Save As and Save & Apply, and field evidence. English and Indonesian sources and `docs/` compatibility mirrors stay synchronized.
- **Source reconciliation**: retains current `master` behavior for Signal Map loading, saving and explicit activation, SCADA engineering scaling guards, source value identity, unmapped IOA evidence, and IEC101 DFC busy/recovery. Help/UI and documentation integration do **not** redefine protocol or scaling logic.
- **Regression evidence**: Windows Release build, C# formatting, xUnit, IEC frame example validator, protocol smoke, native WPF Help/Signal Map screenshot checks, CodeQL, repository hygiene, and dependency review passed for PR #265. All 10 associated review threads were addressed. The public package workflow independently rebuilds/tests the tagged source and generates a portable ZIP, installer, SBOM, SHA256SUMS and build attestations before publication.

## Important release-candidate limitations

- **Not a field-certified stable release.** Live IEC101/103/104 RTU/SCADA integration, 1200-bps SOE completeness, failover, and customer-specific primary engineering calibration still require supervised field FAT. Synthetic CI and Windows installer/updater tests are not IEC conformance evidence.
- **Physical Windows 125%/150% display-scale acceptance and full browser-visual qualification are pending.** CI checked real WPF rendering at logical DIP viewports (including 780x540); do not substitute these screenshots for physical OS DPI evidence.
- **Station data and Q ranges are source-specific.** Bundled Feeder Q ±2078 MVar and Lab SOGI Q ±260 MVar, POAQ inbound normalized 0..1200 and POOP outbound 0..1000 remain reference mappings, **not verified universal calibration**. Verify original raw IEC CA/IOA/TI/QDS and approved SCADA values before activating a profile. Loading/saving a profile does not activate it; use explicit **Save & Apply** when justified. Never send commands to live RTU/SCADA without authorized site procedures.
- A user-owned Signal Map JSON is not automatically overwritten by installation or upgraded to a new calibration. Preserve backups and evidence.

## Public download and verification

After the **source-bound** release workflow passes, this release publishes:
- `ARIEC60870-v3.6.17-rc.5-setup-win-x64.exe` — Windows installer
- `ARIEC60870-v3.6.17-rc.5-win-x64.zip` — self-contained Windows portable ZIP (preferred for isolated evaluation)
- `ARIEC60870-v3.6.17-rc.5-sbom.spdx.json` — SPDX dependency inventory
- `SHA256SUMS.txt` — SHA-256 integrity checks

Compare downloaded bytes to `SHA256SUMS.txt`. Review the GitHub Actions package run and provenance attestations. This is an evaluation prerelease; do not infer production field readiness merely from a green workflow.

**Source:** [PR #265](https://github.com/masarray/arsee/pull/265) · [releases](https://github.com/masarray/arsee/releases) · [Wiki](https://masarray.github.io/arsee/wiki.html).
