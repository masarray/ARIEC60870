# Public release notes

- **Latest public stable:** [ArSee v3.6.18](https://github.com/masarray/arsee/releases/tag/v3.6.18) — official Windows x64 **ArSee-named** installer, portable ZIP, SHA-256, SPDX SBOM and build provenance.
- **Previous stable:** [v3.6.17](https://github.com/masarray/arsee/releases/tag/v3.6.17) (historical Windows filenames retained for release integrity).
- **Archived prerelease:** [v3.6.17-rc.7](https://github.com/masarray/arsee/releases/tag/v3.6.17-rc.7).
- [v3.6.18 release notes](archive/release-notes/RELEASE_NOTES_v3.6.18.md) · [exact-source Package #44645100994](https://github.com/masarray/arsee/actions/runs/44645100994) · [source CI, native updater and real legacy migration FAT #44644140702](https://github.com/masarray/arsee/actions/runs/44644140702) · [Changelog](../CHANGELOG.md).

**Published 20 November 2026.** v3.6.18 fixes the operator-reported Windows install branding regression: only `ArSee.exe` and one `ArSee` shortcut remain after a validated old-v3.6.17 upgrade. New installations default to `Programs/ArSee`; existing installations may retain their old installation folder for safe in-place migration. The product name, uninstaller entry, downloadable setup, ZIP and SBOM now use **ArSee**.

**One-time manual update from v3.6.17:** close active capture/IEC sessions, download the v3.6.18 installer and install it *over* the existing version. **Do not uninstall first or delete `%LOCALAPPDATA%\ARIEC60870`**; that historical directory stores user-owned data. Old v3.6.17 updater cannot automatically discover the renamed installer file. Future ArSee updates use strict ArSee asset validation.

**Boundary:** This is tested Windows software packaging, not IEC conformance or physical SICAM/RTU FAT. Site-specific calibration, live 1200-bps SOE and Windows DPI qualification remain separate.
