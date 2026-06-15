# ARIEC60870 Release Packaging

This document explains the release assets available for users and the automation available for maintainers.

## Assets users should download

GitHub Releases can contain two Windows packages:

```text
ARIEC60870-vX.Y.Z-win-x64-portable.zip
ARIEC60870-vX.Y.Z-win-x64-singlefile.zip
SHA256SUMS.txt
```

Use the **portable** package for the most conservative Windows desktop experience. Use the **singlefile** package when a smaller executable layout is preferred.

## How to run

1. Download a ZIP from GitHub Releases.
2. Extract it to a local folder.
3. Run `Start-ARIEC60870.bat`.
4. Select IEC-101, IEC-103, or IEC-104 in **Setup**.
5. Configure COM/TCP endpoint, address profile, timeout, GI, and optional mapping profile.
6. Start the session and review the evidence workspaces.

## Verifying the download

`SHA256SUMS.txt` is included with each package build so downloaded assets can be verified against their checksum.

## Building packages locally

From repository root:

```powershell
pwsh ./scripts/publish-windows-portable.ps1 -Version 3.6.5
pwsh ./scripts/publish-windows-portable.ps1 -Version 3.6.5 -SingleFile
```

Expected output:

```text
artifacts/release/ARIEC60870-v3.6.5-win-x64-portable.zip
artifacts/release/ARIEC60870-v3.6.5-win-x64-singlefile.zip
artifacts/release/SHA256SUMS.txt
```

Verify package structure:

```powershell
pwsh ./scripts/verify-release-package.ps1 -PackagePath artifacts/release/ARIEC60870-v3.6.5-win-x64-portable.zip
pwsh ./scripts/verify-release-package.ps1 -PackagePath artifacts/release/ARIEC60870-v3.6.5-win-x64-singlefile.zip
```

## GitHub Actions package flow

The repository includes:

```text
.github/workflows/release-package.yml
```

Manual release run:

1. Open **Actions**.
2. Select **Build Windows release packages**.
3. Click **Run workflow**.
4. Leave `version` empty to use `Directory.Build.props`, or provide `X.Y.Z`.
5. Keep **Create or update GitHub Release** enabled when assets should appear on the Releases page.
6. Keep **Also build single-file executable package** enabled when both ZIP variants are needed.
7. Select pre-release or draft status as needed.
8. Run the workflow.

Tag release run is also supported:

```bash
git tag v3.6.5
git push origin v3.6.5
```

On tag push, the workflow infers the version from the tag, builds the packages, verifies structure, uploads workflow artifacts, and creates or updates the GitHub Release.

## Package contents checklist

A complete package includes:

- desktop app executable and runtime files;
- command-line tools;
- `Start-ARIEC60870.bat`;
- `Open-CLI-Help.bat`;
- quick-start, troubleshooting, validation, and packaging documents;
- sample mapping profiles;
- neutral IEC-101/104 example profile;
- `README.md`, `CHANGELOG.md`, `LICENSE`, `NOTICE`, and third-party notice file;
- checksum file in the release artifact set.


## Supply-chain release artifacts

The GitHub release workflow produces more than executable ZIP files. A release run should publish:

- `ARIEC60870-vX.Y.Z-win-x64-portable.zip`
- `ARIEC60870-vX.Y.Z-win-x64-singlefile.zip` when enabled
- `ARIEC60870-vX.Y.Z-sbom.spdx.json`
- `SHA256SUMS.txt`

The workflow also requests build provenance attestation for the ZIP, checksum, and SBOM artifacts. This helps release consumers verify that published assets came from the repository workflow rather than from an opaque local machine.

Local SBOM generation:

```powershell
pwsh ./scripts/generate-sbom-lite.ps1 -Version 3.6.5 -OutputPath artifacts/release/ARIEC60870-v3.6.5-sbom.spdx.json
```
