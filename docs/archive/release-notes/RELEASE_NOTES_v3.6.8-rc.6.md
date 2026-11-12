# ArSee v3.6.8-rc.6 — Premium UI consistency and IEC standards identity

**Planned public prerelease · Windows x64 · engineering evaluation.** This document is release preparation, not evidence of publication. Publish only after the exact release-package workflow has passed and attached immutable artifacts.

## Changes since v3.6.8-rc.5

### User interface
- **Shared navbar pill geometry:** one global design-token authority for text-action buttons: 42-DIP height, 19-DIP corner radius, 16-DIP horizontal padding and 17-DIP inner radius. Primary/Secondary surfaces no longer collapse visually because of excess outer margin.
- **Event Journal:** Include, Exclude, Latest, Export CSV and Search use the approved pill proportions. The indexed Event Log, bounded history search, original-frame evidence, pagination and safe CSV export from RC.5 remain intact.
- **Pagination:** 10-DIP rounded Lucide chevrons centered in dedicated 32-DIP circular hit targets, including restrained hover and disabled states; these controls are intentionally not pill-shaped.
- **Windows icons:** corrected the alpha perimeter and colored/gray fringe on all six generic ICO sizes (16/32/48/64/128/256). The three protocol PNG badges remain unchanged where already alpha-correct.
- **Navigation:** removed confusing technical pop-up tooltips from tabs and improved quiet selected/hover visuals. The Status History footer is edge-docked with top-only rounded corners and bounded/ellipsis summary text.
- **Correct IEC identity:** native title bar, app heading, Windows EXE metadata, bilingual public website and README use **IEC 60870-5-101/103/104 Protocol Tester & Analyzer**, including the IEC 60870-5- prefix.

### Scope and compatibility
- The underlying IEC-101/103/104 protocol engine and Event Journal persistence behavior are not changed by these visual commits. The unmerged **draft PR #145** shared IEC-101 transaction/FCB work is **excluded**.
- Do not equate IEC-101 indexed event rows with guaranteed device-generated SOE delivery. Physical SICAM A8000 serial/FIFO tests at 1,200 bps are **not certified** by these software checks.
- These are unsigned, Apache-2.0 Windows x64 test packages for an authorized, isolated engineering bench; not field conformance certification.

## Publication assets — not available until release succeeds

- `ARIEC60870-v3.6.8-rc.6-setup-win-x64.exe` — per-user installer.
- `ARIEC60870-v3.6.8-rc.6-win-x64.zip` — portable Windows x64 ZIP.
- `SHA256SUMS.txt` — matching hash values for the exact RC.6 assets.
- `ARIEC60870-v3.6.8-rc.6-sbom.spdx.json`, provenance attestations, six xUnit suite results and WPF screenshots/metrics.

### Software evidence
The pre-release merged GUI baseline `9cf621d10b7579543b3b63330e5358c5964c8c3e` passed [Windows CI #43138763275](https://github.com/masarray/ARIEC60870/actions/runs/43138763275), [CodeQL #43138763359](https://github.com/masarray/ARIEC60870/actions/runs/43138763359) and repository hygiene. A later documentation reconciliation PR #173 also passed CI and CodeQL without reverting those GUI commits.

The existing Windows WPF acceptance exercises synthetic frames, Event Journal and 10,000-frame HOLD/AUTO behavior. **Its CI runner did not honor every requested 1366×768/1540×960 window size**, so this is not physical-DPI or full-resolution certification. The actual RC.6 published executable must separately pass the `Package` workflow acceptance on its exact versioned bytes.

## Engineer's RC verification

1. Verify the new `SHA256SUMS.txt` before installing, and fully extract a portable ZIP before launching.
2. Confirm the app window title includes `IEC 60870-5-101/103/104`. Inspect nav pills, Event Journal action pills, small circular pagination controls, Windows icons, and bottom Status History while resizing the window.
3. Validate Include/Exclude, Previous/Next/Latest, search and CSV export; verify original frame evidence and known source-quality indicators.
4. Validate Frame Trace HOLD/AUTO on a simulator and ensure session data and installed-user settings persist across an upgrade.
5. On authorized isolated hardware only, compare actual RTU/SCADA frames with external counters; submit sanitized traces, device settings and version when reporting a problem.

Feedback: https://github.com/masarray/ARIEC60870/issues

**Publication rule:** RC.5 public bytes remain immutable. RC.6 requires a new source SHA, tag, Windows installer, portable ZIP, matching checksum/SBOM, build attestations and matching direct download links after successful publication.
