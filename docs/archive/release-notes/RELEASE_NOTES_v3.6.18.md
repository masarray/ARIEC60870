# ArSee v3.6.18 — One Windows app name, safe installer migration

**Windows x64 · public stable release · software-qualified.** This immutable release supersedes v3.6.17 only when its official GitHub Release is published with matching checksums, build provenance and Windows acceptance evidence.

## Fixed: legacy ARIEC60870 name and duplicate shortcuts

Based on an operator's genuine v3.6.17 Windows installation screenshots, the previous installer could leave both `ARIEC60870` and `ArSee` Start Menu shortcuts while keeping the actual application filename `ARIEC60870.exe`. v3.6.18 fixes the externally visible installed identity:

- **One executable:** `ArSee.exe` (not `ARIEC60870.exe`).
- **One Start Menu entry:** **ArSee**, in a new **ArSee** program group. Old known `ARIEC60870` and misgrouped `ArSee` shortcuts are removed on upgrade.
- **Clean new installations:** install under `%LOCALAPPDATA%\Programs\ArSee` by default. An existing v3.6.17 installation may retain its original *installation-directory path* for in-place compatibility, but contains only the newly named `ArSee.exe`. This deliberate path retention avoids breaking existing Inno uninstall/rollback metadata.
- **New public downloadable assets:** `ArSee-v3.6.18-setup-win-x64.exe`, `ArSee-v3.6.18-win-x64.zip`, matching SHA256 checksums, and an ArSee-branded SPDX SBOM. Historical v3.6.17 GitHub Release bytes and filenames remain unchanged.
- Windows Add/Remove Programs continues to present the product as **ArSee**, with the same stable Inno AppId and a single versioned uninstall registration.

## Upgrade from v3.6.17 (one-time manual installer)

1. Save or close active IEC-101/103/104 sessions and ensure evidence/capture writing has stopped safely.
2. Download the **v3.6.18 ArSee installer** from the official ArSee landing page or [GitHub Releases](https://github.com/masarray/arsee/releases), and compare its SHA256 with the corresponding `SHA256SUMS.txt`.
3. **Run the new setup directly over the existing installation**; do not remove v3.6.17 first and do not delete its program/data folders manually.
4. Confirm that the Start Menu contains **ArSee** and that the installed program launches as `ArSee.exe`. Your previous Signal Lists, profiles, evidence/captures, and settings must remain accessible.

**Why the one-time manual step?** The already-published v3.6.17 built-in updater recognizes the old immutable `ARIEC60870-v...` asset naming. It cannot discover the renamed `ArSee-v3.6.18-setup-win-x64.exe` automatically. Once v3.6.18 is installed, its new updater recognizes future **ArSee**-named stable assets using strict official URLs, matching filename, SHA256 and declared size.

## Source-qualified acceptance and boundaries

- Windows Release build, full protocol/xUnit regressions, actual WPF smoke and portable package checks.
- **Real Windows historical upgrade test:** install authentic, checksum-verified public v3.6.17, recreate both legacy shortcuts, upgrade to the candidate built from the exact release source, assert one `ArSee.exe`, one `ArSee` shortcut, no old EXE or shortcut, one uninstall entry, retained user-data sentinel, successful WPF startup, and clean uninstall that preserves user data.
- Independent genuine previous-release updater worker FAT: checks the installed-version restart/acknowledgement, rollback protections and preserved data.
- CodeQL, dependency review, repository hygiene, Windows actionlint, GitHub Release package SHA256/SBOM and build-provenance gates.

**Not changed:** the IEC-101/103/104 wire protocol and active-session behavior. Internal C# project/namespace names and `%LOCALAPPDATA%\ARIEC60870` application-data root are retained intentionally for source and user-data compatibility. Do not delete or rename the data root.

**Not claimed:** physical 1200-bps SICAM/SCADA/RTU FAT, live switchgear command acceptance, full IEC conformance certification, or real-device field soak. Test IEC controls only with authorized isolated/simulated hardware.
