# ArSee v3.6.17-rc.7 — Clear, short operator tooltips

**Public Windows x64 release candidate · 19 November 2026**

This prerelease is source-bound to reconciled `master` after [PR #274](https://github.com/masarray/arsee/pull/274). It contains all preceding [RC.6](https://github.com/masarray/arsee/releases/tag/v3.6.17-rc.6) features, including the Latest Event auto-follow visual fix, bilingual Help Center R4.1, and the preserved IEC101/103/104 and Signal Map improvements.

## What is easier to read

- **Events FC10:** short hover tooltip **“Class 1 Data Polling”**
- **Events FC11:** short hover tooltip **“Class 2 Data Polling”**
- FC labels still reflect only an actually observed, qualified IEC-101 primary request and secondary response. **The tooltip is not a statement about any IOA's configured class.** The exact evidence, raw TX/RX and `RequestFC` CSV remain available for investigation.
- 33 verbose static MainWindow action/setup tooltips were rewritten into short, understandable microcopy covering Events, Trace, serial setup, protocol interpretation, redundancy, passive monitoring and the command workspace. Additional dynamic action hints (Latest Event, Connect, Capture, Signal Map, update) were simplified.
- **Protected source-backed exceptions:** optimized IOA/CA/address tooltips, quality, field-level raw frame evidence, selected inspector, diagnostics counters and exact file paths are intentionally not truncated. No per-row tooltip factory or new decoder/scaling code was introduced.
- Permanent `UX-TOOLTIP-01` policy in `AGENTS.md` and `docs/RESPONSIVE_LAYOUT_POLICY.md`: routine action hover is 2–8 straightforward words, XAML literal tooltips <=72 characters, and forensics/provenance exceptions remain detailed.

## Engineering verification

[PR #274](https://github.com/masarray/arsee/pull/274) was squash-merged into `master` at `2cb7c42735c691c4f2411b0716e263d9b2365d93`. Exact-head PR CI, warning-as-error Windows build, format gate, protocol smoke, full xUnit (including exact FC10/FC11 + source-match tests), native Signal Map WPF checks, CodeQL, dependency review and hygiene passed. [Explicit full Windows acceptance #44476667883](https://github.com/masarray/arsee/actions/runs/44476667883) passed portable publish, passive GUI smoke, OBS-C real-WPF viewport/1400-frame acceptance and artifact generation. The release workflow independently builds/tests/packages and attests the tagged RC.7 source before publication. Release assets must not be considered published until that source-bound packaging workflow succeeds.

No IEC101/103/104 wire engine, Event Journal source/correlation/retention, SCADA scaling or Signal Map `Save & Apply` behavior changes were made for this tooltip release.

## Important prerelease limits

**RC.7 is not field-certified stable software.** Actual IEC-101/103/104 RTU/IED/SCADA field FAT, real 1200-bps SOE completeness, station-specific SCADA scaling calibration, and actual Windows 125%/150% DPI operator acceptance are still separate, unproven qualifications. Hosted CI screenshots of WPF *logical DIP* viewports do not certify physical monitor DPI. Do not issue live commands without authorized site procedures. Existing user-owned profile data is not silently overwritten.

## Downloads and verification

Upon successful source-bound GitHub Release publishing, use:
- `ARIEC60870-v3.6.17-rc.7-setup-win-x64.exe` — Windows installer.
- `ARIEC60870-v3.6.17-rc.7-win-x64.zip` — portable Windows ZIP.
- `ARIEC60870-v3.6.17-rc.7-sbom.spdx.json` — dependency inventory.
- `SHA256SUMS.txt` — integrity checks.

Compare the installer/ZIP to the published SHA-256 checksum. The prior RC.6 release remains immutable and available.
