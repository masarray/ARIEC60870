# Public release notes

## Latest public stable: ArSee v3.6.20 (21 November 2026)

**[Official v3.6.20 release](https://github.com/masarray/arsee/releases/tag/v3.6.20)** · [Installer Windows x64](https://github.com/masarray/arsee/releases/download/v3.6.20/ArSee-v3.6.20-setup-win-x64.exe) · [Portable ZIP](https://github.com/masarray/arsee/releases/download/v3.6.20/ArSee-v3.6.20-win-x64.zip) · [SHA-256](https://github.com/masarray/arsee/releases/download/v3.6.20/SHA256SUMS.txt) · [SBOM](https://github.com/masarray/arsee/releases/download/v3.6.20/ArSee-v3.6.20-sbom.spdx.json).

Published as **Public Stable** from immutable source `081746bb5e1f9d4c1511ae5769d4299565c5b17d`. [Windows package / provenance #44898156357](https://github.com/masarray/arsee/actions/runs/44898156357), [source CI and native updater / legacy migration FAT #44897646916](https://github.com/masarray/arsee/actions/runs/44897646916) and [CodeQL #44897646866](https://github.com/masarray/arsee/actions/runs/44897646866) succeeded. Subsequent website changes do not modify the stable tag or binary assets.

### Tested operator improvements

- **IEC-101 Auto-GI:** retain configured, not-yet-attempted first-Connect GI after initial COM/link recovery; respect DFC busy, A/B active owner, explicit GI OFF and passive RX-only; never replay delivery-ambiguous TX.
- **Connection Setup:** Enter immediately after first opening runs the existing Connect button, even if focus has not yet moved from the sidebar; Disconnect leaves Setup visible and successful Connect closes it.
- **Redundancy:** compact, aligned timeline rows with complete selected Reason/Result.
- **Events:** Include/Exclude/search selections remain effective from the first event of new connection sessions.
- **INT/scaling:** compact IEC-101/104 signed TI11/12/35 cells show numeric values without the `Scaled=` decoder label; calibrated Signal Mapping and original evidence remain intact.

[Detailed release notes](archive/release-notes/RELEASE_NOTES_v3.6.20.md) · [Integrated PR #305](https://github.com/masarray/arsee/pull/305) · [Changelog](../CHANGELOG.md).

### Earlier immutable releases

- **Previous public stable:** [v3.6.19](https://github.com/masarray/arsee/releases/tag/v3.6.19), including Class-1 ACD-based pending buffer evidence and Windows legacy-install migration ([historical notes](archive/release-notes/RELEASE_NOTES_v3.6.19.md)).
- **Earlier stable:** [v3.6.18](https://github.com/masarray/arsee/releases/tag/v3.6.18) and [v3.6.17](https://github.com/masarray/arsee/releases/tag/v3.6.17).
- **Superseded prerelease:** [v3.6.20-rc.1](https://github.com/masarray/arsee/releases/tag/v3.6.20-rc.1).

### Safe upgrade and boundaries

Close IEC sessions/captures before upgrading. For registered older installations under `Programs/ARIEC60870`, run the **official v3.6.20 installer directly** for supported migration to `Programs/ArSee`. Do not manually uninstall or delete `%LOCALAPPDATA%\ARIEC60870`—it contains operator profiles, mappings and captures. Extract the portable ZIP into its own folder, never over a running installation.

The operator confirmed the improvements on the tested configuration. This is **not** multi-vendor IEC certification, universal RTU FIFO age evidence, energized-control proof, or independent FAT of all hardware interfaces. Original frames and the project-specific FAT/SAT plan remain authoritative.
