# ArSee v3.6.17-rc.1 — Signal Map dropdown and toolbar acceptance

**Release candidate · Windows x64 · 18 November 2026**

This pre-release is for UI evaluation of the latest Signal Map engineering workbench. It is **not** a stable release, a protocol conformance certificate, or physical RTU/SCADA FAT evidence. The latest published stable fallback is [v3.6.16](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.16).

## Scope since stable v3.6.16

- [PR #255](https://github.com/masarray/ARIEC60870/pull/255) integrates the **More** circular icon-only action at 36×36 DIP, optically aligned with adjacent Add, Save, and Save & Apply 36-DIP pill controls. It inherits the same Main Window circular appearance and input-state template.
- Technical Signal Map fields now expose a **compact native WPF dropdown** of common IEC values with readable short labels and a secondary explanation for each option: **IEC Type ID (TI)**, **IEC Type/role**, **Class**, **COT**, **Command policy**, **TYPE/category**, **Engineering Unit**, and **Scaling Mode**.
- Each selection saves the **exact existing field code**, not a UI description: e.g. TI 31 saves `31`; IEC role `M_DP_TB_1` and policy `MonitorOnly` remain independent values. Non-catalogued project-specific codes remain editable. The UI never auto-pairs TI, role, unit, scaling, command policy or calibration from another choice.
- Retains the stable grid-first workbench: native spreadsheet editing, filtering, Save/Apply validation, resizable inspector, paired two-point scaling, and current user-owned JSON profile schema. No IEC-101/103/104 parser, TX state machine, GI policy, physical serial wiring, command permission or source evidence changes.

## Recommended desktop test (Signal Map)

1. **Back up the current engineering profile JSON** first. For an isolated side-by-side evaluation, use the portable ZIP rather than upgrading a production installation.
2. Open Signal Map and check that **More** remains circular and is exactly as tall as adjacent action pills.
3. Choose digital TI 30 / TI 31 and analog TI 9 / TI 11 / TI 13 from the Mapping inspector; verify option descriptions and that the selected cell/JSON retain numeric codes.
4. Check role, Class, COT, policy and category dropdowns; confirm no adjacent fields are silently rewritten. Choose an unfamiliar project-specific token, then verify edit/reopen fidelity.
5. Confirm Scaling tab still exposes Factor and TwoPoint workflows with X0/X100/Y0/Y100; avoid interpreting IEC TI11 signed counts as MVar without authoritative field calibration.
6. Confirm Save, Save & Apply, Copy/Paste, Undo/Redo, profile loading and the 31-point default profile have not regressed. If testing with an RTU, perform only site-authorized, isolated operations and retain source HEX.

## Qualification and release invariants

The merged PR #255 passed Release build, formatting, protocol smoke, xUnit, native WPF acceptance, CodeQL, dependency review and repository hygiene; this RC's own Package workflow independently builds and checks its **release source branch commit** before publishing. Windows physical DPI 125/150%, genuine relay command transactions, SICAM serial handshakes, SOE/FIFO integrity, and verified field scaling remain **NOT TESTED** by these GUI gates. Unmerged IEC-101 P0 work in [PR #145](https://github.com/masarray/ARIEC60870/pull/145) is explicitly excluded.

Use the GitHub Release assets when and only when the Package run passes: `ARIEC60870-v3.6.17-rc.1-setup-win-x64.exe`, `ARIEC60870-v3.6.17-rc.1-win-x64.zip`, `SHA256SUMS.txt`, and SPDX SBOM. The immutable `v3.6.17-rc.1` tag must refer to the accepted candidate source; do not replace the tag or republish different bytes under it.
