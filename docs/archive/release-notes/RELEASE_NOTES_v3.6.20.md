# ArSee v3.6.20 — IEC-101 startup reliability and operator workflow refinement

**Windows x64 · Public Stable · 21 November 2026**

ArSee is a free, Apache-2.0 IEC 60870-5-101/103/104 protocol tester and evidence analyzer. This update builds on [v3.6.19](https://github.com/masarray/arsee/releases/tag/v3.6.19) and the archived [v3.6.20-rc.1](https://github.com/masarray/arsee/releases/tag/v3.6.20-rc.1); neither earlier tag nor its public binaries are modified.

## What's fixed

- **IEC-101 first-Connect Auto-GI:** keep an explicitly enabled, not-yet-attempted TI100/COT6 intention across early COM/open/recovery and secondary link verification; check DFC busy and active A/B ownership before sending; never automatically replay an attempted or delivery-ambiguous GI after reconnection/failover. Respect the operator's saved OFF and keep IEC-101 Passive RX-only. Preserve verified ACD/DFC/FCB, Class 1/2 and observed-CA GI evidence/recovery policy.
- **GI evidence remains inspectable:** link health or a GI counter alone never establishes success. Keep original source-qualified TX TI100/COT6, matching ACTCON COT7, COT20 process observations and ACTTERM COT10, with the correct station/address interpretation.
- **Redundancy timeline:** compact, aligned, virtualized single-line rows and predictable column proportions; the selected row preserves full Reason/Result diagnostics.
- **Connection Setup keyboard and lifecycle:** pressing Enter immediately after first opening Setup now runs the same Connect button action, including when keyboard focus is still on the sidebar Setup button; works across IEC-101 single/dual/passive, IEC-103 and IEC-104. Enter never disconnects an already running session. Disconnect keeps Setup visible; successful Connect closes it; invalid settings remain editable.
- **Event History filters:** Include/Exclude/search preferences apply from the first event of each new connection session. Incremental filtered indexes repair a stale unfiltered reset without dropping retained raw evidence or scanning the entire history on each refresh.
- **IEC-101/104 scaled INT operator presentation:** Type 11/12/35 compact cells show signed numeric values (e.g. INT 4 instead of the decoder label `Scaled=4`). Signal Mapping engineering scaling still takes precedence, and full original decoder/evidence is preserved.

## Qualification and field verification

- The operator tested the integrated Windows evaluation build and reported all requested GUI, GI and event improvements working as expected, including the first-open Enter scenario. The reported field scope is limited to the tested configuration; it is **not** a multi-vendor IEC conformance certificate or proof of energized-switchgear operation.
- PR [#305](https://github.com/masarray/arsee/pull/305) includes deterministic IEC-101 single/A/B GI and DFC regression scenarios, filtered reconnect/INT output tests and real-WPF keyboard acceptance for all five profile variants. Full source-bound Windows CI, CodeQL, actual installed-updater/branding migration FAT, release packaging, SHA-256, SBOM and provenance remain mandatory publication gates. Published artifact checks/results are in the GitHub Actions runs for this exact release SHA, not the prior evaluation build.

## Download, upgrade and integrity

The official published stable release supplies `ArSee-v3.6.20-setup-win-x64.exe`, `ArSee-v3.6.20-win-x64.zip`, `ArSee-v3.6.20-sbom.spdx.json` and `SHA256SUMS.txt`. Verify the installer or ZIP against the same-release checksum manifest before use. Close active IEC sessions prior to upgrading; the official Inno Setup installer handles supported previous ArSee installations without deleting operator data in `%LOCALAPPDATA%\ARIEC60870`. Portable ZIP is intended for an isolated folder, not over-writing a running installation.

**Scope limits:** IEC-101 active unbalanced single and A/B are supported; active balanced IEC-101 and universal multi-vendor physical FAT are not claimed. Event buffer indication reflects observed link/service evidence, not individual RTU FIFO age. No silent automatic control replay or passive transmission is introduced.
