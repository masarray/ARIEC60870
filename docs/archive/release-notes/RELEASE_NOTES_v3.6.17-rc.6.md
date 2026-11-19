# ArSee v3.6.17-rc.6 — Event History Latest Event visual state

**Public Windows x64 prerelease candidate · 19 November 2026**

This release is cut from reconciled `master` after [PR #273](https://github.com/masarray/arsee/pull/273), preserving all [v3.6.17-rc.5](https://github.com/masarray/arsee/releases/tag/v3.6.17-rc.5) Help Center R4.1, IEC engineering handbook and Signal Map behavior.

## Latest Event button improvement

- **Active** — the **Latest Event** button shows a subtle blue selected fill, clearer border and readable dark-blue text only when actual visible Event History auto-follow is running.
- **Inactive** — the button returns to the same premium neutral pill as the other action buttons after navigating to First/older pages or deliberately scrolling upward.
- **Selection-aware** — inspecting a selected Event row suspends visible auto-follow and clears the active color; releasing selection restores active styling only if auto-follow policy remains enabled.
- **Resumption** — clicking Latest Event jumps to the newest matching indexed events and resumes following; scrolling to the bottom of the actual latest page can also resume. Older page-bottom never jumps unexpectedly to newest.
- **Reliable style** — hovering/pressing no longer masks the active fill. Tooltip and UI Automation status match active/inactive. State is projected from the existing Event History policy; there is no second auto-scroll engine, timer, or toggle state.

## Engineering correctness and verification

The change is limited to WPF Event History UI, premium style, native acceptance tests and the documented Event Log contract. It does not alter IEC101/103/104 protocols, RTU communications, event acquisition, indexed journal durability, signal identity, engineering scaling, Signal Map Save/Save & Apply or other core functionality.

- [PR #273](https://github.com/masarray/arsee/pull/273) exact-head CI, formatter, protocol smoke, xUnit, CodeQL, dependency and repository hygiene: **PASS**.
- [Explicit full Windows CI](https://github.com/masarray/arsee/actions/runs/44459831044) tested pinned source `d31d90feeb1d6d91d15f8447518f14b8f43a6e1a`, including self-contained WPF `--passive-gui-smoke` Event History state transitions and OBS-C real-WPF viewport acceptance: **PASS**.
- Exact **public release source/installer** must additionally pass the publishing workflow's own full xUnit, executable GUI smoke, WPF Frame Trace acceptance, SBOM, SHA-256 integrity and provenance attestations. Those assets are only released if that workflow passes.

## Known qualification limits

**This is a public prerelease candidate, not field-certified stable production software.** Actual RTU/IED/SCADA FAT, 1200-bps SOE completeness, 125%/150% physical Windows display-scaling checks and station-specific engineering scaling/calibration remain dependent on operator evidence. Never infer IEC conformance or permission for live RTU commands from synthetic tests.

Bundled Feeder Q, Lab SOGI Q, POAQ and POOP references retain the source-specific caveats documented in RC.5; user-owned signal profiles are not silently overwritten. Verify exact CA/IOA/TI/QDS and independent SCADA readings before applying mappings.

## Downloads

After successful public release workflow, download the matching:
- `ARIEC60870-v3.6.17-rc.6-setup-win-x64.exe` — Windows installer
- `ARIEC60870-v3.6.17-rc.6-win-x64.zip` — portable Windows x64 ZIP (preferred for isolated testing)
- `ARIEC60870-v3.6.17-rc.6-sbom.spdx.json`
- `SHA256SUMS.txt`

Verify installer/ZIP hashes against the published SHA-256 checksum file. RC.5 remains immutable and downloadable.
