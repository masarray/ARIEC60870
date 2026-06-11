# ARIEC60870 Release Assets

This document explains the release assets available for users and the packaging automation available for contributors.

## Assets users should download

For normal use, download the Windows portable ZIP from GitHub Releases:

```text
ARIEC60870-vX.Y.Z-win-x64-portable.zip
SHA256SUMS.txt
```

The portable ZIP contains the desktop app, CLI tools, sample files, documentation, and license files. It is intended for users who want to try ARIEC60870 without opening Visual Studio.

## How to run the portable package

1. Download the ZIP from GitHub Releases.
2. Extract it to a local folder.
3. Run `Start-ARIEC60870.bat`.
4. Configure the relay communication settings in **Setup**.
5. Start the session and review the evidence screens.

## Verifying the download

`SHA256SUMS.txt` is included with each package build so the downloaded ZIP can be verified against its checksum.

## Building a package locally

From repository root:

```powershell
pwsh ./scripts/publish-windows-portable.ps1 -Version 1.3.0
```

Expected output:

```text
artifacts/release/ARIEC60870-v1.3.0-win-x64-portable.zip
artifacts/release/SHA256SUMS.txt
```

Verify package structure:

```powershell
pwsh ./scripts/verify-release-package.ps1 -PackagePath artifacts/release/ARIEC60870-v1.3.0-win-x64-portable.zip
```

## GitHub Actions package flow

The repository includes this workflow:

```text
.github/workflows/release-package.yml
```

Manual release run:

1. Open **Actions**.
2. Select **Build Windows portable package**.
3. Click **Run workflow**.
4. Set `version`, for example `1.3.0`.
5. Keep **Create or update GitHub Release** enabled when the package should appear on the Releases page.
6. Select pre-release or draft status as needed for the package maturity.
7. Click **Run workflow**.

The workflow builds the portable ZIP, verifies package structure, uploads a workflow artifact, creates tag `vX.Y.Z` when needed, and publishes these assets to GitHub Releases:

```text
ARIEC60870-vX.Y.Z-win-x64-portable.zip
SHA256SUMS.txt
```

Tag release run is also supported:

```bash
git tag v1.3.0
git push origin v1.3.0
```

## Package contents checklist

A complete portable package includes:

- desktop app executable and runtime files;
- command-line tools;
- `Start-ARIEC60870.bat`;
- quick-start and troubleshooting documents;
- samples and mapping profile example;
- `LICENSE`, `NOTICE`, and third-party notice file;
- release notes and checksum file.
