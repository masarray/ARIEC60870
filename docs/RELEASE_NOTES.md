# Public release notes

- **Latest public stable:** [ArSee / ARIEC60870 v3.6.14](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.14) — Windows installer, portable ZIP, SHA-256, SPDX SBOM and provenance.
- [Detailed v3.6.14 release notes](archive/release-notes/RELEASE_NOTES_v3.6.14.md).
- **Previous stable:** [v3.6.13](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.13).
- [Exact-source native updater FAT](https://github.com/masarray/ARIEC60870/actions/runs/44048682483) · [public Package workflow](https://github.com/masarray/ARIEC60870/actions/runs/44049527667).
- [Changelog](../CHANGELOG.md).

v3.6.14 ships IEC-101/104 engineering-unit Factor/TwoPoint scaling, document-backed 31-point SCADA signal names and an opt-in, non-destructive Sync 31 I/O migration for older profiles. TI11 reactive-power integer counts are **not** treated as actual MVar without project-authorized gateway calibration. Physical RTU and multi-vendor FAT remain unqualified; historical draft PR #145 remains unmerged.
