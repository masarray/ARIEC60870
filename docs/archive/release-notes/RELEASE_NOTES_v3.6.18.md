# ArSee v3.6.18 — Windows Identity & Installer Migration

**Candidate only — NOT YET RELEASED.** Public latest stable remains v3.6.17 until an exact-source GitHub Actions release succeeds and new immutable assets are published.

## Operator-reported defect
The Windows v3.6.17 installation could contain two Start Menu shortcuts under a legacy ARIEC60870 group and install the executable as ARIEC60870.exe even while the UI was branded ArSee.

## Fixed in candidate source
- User executable and shortcuts: **ArSee.exe** and one **ArSee** Start Menu item. New installs use Programs/ArSee.
- Preserve the historical AppId, user data under `%LOCALAPPDATA%\ARIEC60870`, custom install directory selection, installed user profiles, and SCADA recordings. Safe in-place upgrades may retain the old *folder path* to preserve the exact uninstaller/rollback target; no blind directory move.
- Remove only known legacy ARIEC60870.exe/shortcut files during a successful installer upgrade; do not recursively delete prior settings or journal.
- Future installer, ZIP, and SBOM assets are **ArSee-v3.6.18-...**. Existing v3.6.17 GitHub Release is immutable and keeps historical ARIEC60870 asset filenames; landing page resolver accepts both with exact official URLs/digests.
- The new built-in updater validates only official ArSee release assets with matching SHA256SUMS. **A running v3.6.17 app cannot auto-discover a differently named new installer**; the v3.6.18 installer must be downloaded and installed manually for the one-time transition. Subsequent ArSee updates use the new asset naming.

## Qualification before publication
- [ ] Windows CI, xUnit/smoke, WPF startup and Zip structural check.
- [ ] Inno Setup *real Windows old v3.6.17 -> v3.6.18* migration FAT, single shortcut, no leftover legacy EXE, only one uninstall registration, preserved user data.
- [ ] New-brand updater worker restart/rollback integration, CodeQL, dependency/hygiene, full release packaging, SBOM/attestation.
- [ ] New public release and landing synchronization only after all gates pass.

**Out of scope:** C# internal namespace/project renames, IEC protocol wire formats, user application-data folder migration, hardware SCADA/RTU acceptance.
