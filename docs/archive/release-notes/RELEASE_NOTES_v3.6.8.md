# ArSee v3.6.8 — Public Stable Software Release

**Windows x64 • Apache-2.0 • 12 November 2026**

ArSee is an open-source IEC 60870-5-101/103/104 protocol tester and evidence analyzer. **Stable** describes passing the project's defined software release gate; it is not IEC protocol conformance certification, proof of field SOE completeness, or authorization to test a live substation.

## Downloads and integrity
- **Installer:** `ARIEC60870-v3.6.8-setup-win-x64.exe` — per-user Windows setup.
- **Portable:** `ARIEC60870-v3.6.8-win-x64.zip` — extract the complete directory.
- **SHA256SUMS.txt** — verify downloads before installation.
- **SBOM:** `ARIEC60870-v3.6.8-sbom.spdx.json`, plus provenance attestations.

Use this project's official GitHub Releases page. Unsigned binaries may trigger Windows SmartScreen.

## Changes in 3.6.8

**Five-workspace unified search & compact actions.** Merged [PR #183](https://github.com/masarray/ARIEC60870/pull/183) introduces one native WPF source of truth for the approved 36-DIP toolbar search and pill controls across Raw Frames, Values, Event Log, Controls and GI. Left-aligned search input, transparent inset clear X, restrained hover/pressed red, accessible names and original search/filter handlers are preserved. The navbar, circular pagination and critical command controls retain distinct interaction geometry.

**Event Journal and trace evidence.** One indexed event grid, immutable event numbering, bounded asynchronous history indexing and loss accounting, 100-event pages, full indexed search, Include/Exclude, CSV export, original frame navigation and HOLD/AUTO trace behavior continue from the prior release candidates. Values, protocol detail, timestamps and signal quality remain viewable.

**Brand/installer.** App and EXE identify IEC 60870-5-101/103/104 correctly. This stable-preparation change also fixes the package workflow so **stable** and **RC** releases include a matching Windows Setup EXE and installer provenance, rather than only the prerelease channel.

## Release verification gate
A public release must be built from the **exact tagged SHA** and must pass: Windows warning-gated Release build; six xUnit regression suites and protocol smoke tests; portable artifact verification; real-WPF smoke and 10,000 synthetic-frame HOLD/AUTO acceptance on the published EXE; Setup EXE compilation; SHA256, SPDX SBOM and provenance. These notes alone do not imply the release has been published.

Pre-release baseline `master@365fc03032f6c6bfb8f3af350b5629ed6209efec`: [CI #43152880580](https://github.com/masarray/ARIEC60870/actions/runs/43152880580), [CodeQL #43152880798](https://github.com/masarray/ARIEC60870/actions/runs/43152880798), hygiene passed. The final release must tag a subsequent merged stable-preparation commit and pass its own Package run.

CI screenshots ran with actual WPF windows around 1044 DIP when 1366x768 and 1540x960 were requested, so they do **not** certify physical 1366/1540 display resolution or a specific DPI. GUI tests use synthetic traffic.

**Excluded:** [Draft IEC-101 engine PR #145](https://github.com/masarray/ARIEC60870/pull/145) is not merged into this release. Its shared per-link transaction/FCB, serial re-open/failover and related engineering changes remain separate. Physical SICAM A8000 1200-bps 60/500/2000-SOE FIFO, real COM recovery, independent IEC_Test and multi-vendor FAT are still unverified. Software CI is not proof of actual device-to-SCADA delivery.

## Engineer acceptance
1. Verify SHA256; install or fully extract portable ZIP.
2. Inspect all five workspace searches, Event Journal filters/pagination, title text, transparent search clear-X and icon scaling.
3. Verify CSV, original raw frame links, interpretation, timestamp and quality.
4. Check HOLD/AUTO, saved settings and installer upgrades.
5. Verify physical connections against independent device counters on an isolated authorized bench.

Report sanitized defects at https://github.com/masarray/ARIEC60870/issues

**Release artifacts are immutable.** Never replace a published release's source or binaries; use a new version for further changes.
