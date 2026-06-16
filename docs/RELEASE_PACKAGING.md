# ARIEC60870 Release Packaging

This document explains the user-facing release package and the automation used by maintainers.

## User-facing release asset

A professional public release should keep the download choice simple. The primary Windows asset is:

```text
ARIEC60870-vX.Y.Z-win-x64.zip
```

The ZIP is intended for normal users and contains one self-contained desktop executable:

```text
ARIEC60870.exe
```

No start batch file is required. Users extract the ZIP and double-click the EXE.

## Integrity and supply-chain files

Release automation also publishes:

```text
ARIEC60870-vX.Y.Z-sbom.spdx.json
SHA256SUMS.txt
```

The workflow also requests build provenance attestation for the ZIP, checksum, and SBOM artifacts. This helps release consumers verify that published assets came from the repository workflow rather than from an opaque local machine.

## How users run the app

1. Download `ARIEC60870-vX.Y.Z-win-x64.zip` from GitHub Releases.
2. Extract the ZIP to a local folder.
3. Run `ARIEC60870.exe`.
4. Open **Setup**.
5. Select IEC-101 serial, IEC-103 serial, or IEC-104 TCP/IP.
6. Configure the approved COM/TCP endpoint, addressing profile, timeout, GI, and optional mapping profile.
7. Start the session and review the evidence workspaces.
8. Export the PDF report from **Report**.

## Building the package locally

From repository root:

```powershell
pwsh ./scripts/publish-windows-portable.ps1 -Version 3.6.5
```

Expected output:

```text
artifacts/release/ARIEC60870-v3.6.5-win-x64.zip
artifacts/release/SHA256SUMS.txt
```

Verify package structure:

```powershell
pwsh ./scripts/verify-release-package.ps1 -PackagePath artifacts/release/ARIEC60870-v3.6.5-win-x64.zip
```

## GitHub Actions package flow

The repository includes:

```text
.github/workflows/release-package.yml
```

Manual release run:

1. Open **Actions**.
2. Select **Build Windows single-file release**.
3. Click **Run workflow**.
4. Leave `version` empty to use `Directory.Build.props`, or provide `X.Y.Z`.
5. Keep **Create or update GitHub Release** enabled when assets should appear on the Releases page.
6. Select pre-release or draft status as needed.
7. Run the workflow.

Tag release run is also supported:

```bash
git tag v3.6.5
git push origin v3.6.5
```

On tag push, the workflow infers the version from the tag, builds the single-file Windows package, verifies structure, uploads workflow artifacts, and creates or updates the GitHub Release.

## Package contents checklist

A complete user package includes:

- `ARIEC60870.exe` at package root;
- `README_RELEASE.txt`;
- quick-start, user guide, troubleshooting, validation, and packaging documents;
- neutral sample mapping/profile files;
- `README.md`, `CHANGELOG.md`, `LICENSE`, `NOTICE`, and third-party notice file.

A complete user package must not include batch launchers or multiple executable variants that confuse normal users.

## Local SBOM generation

```powershell
pwsh ./scripts/generate-sbom-lite.ps1 -Version 3.6.5 -OutputPath artifacts/release/ARIEC60870-v3.6.5-sbom.spdx.json
```
