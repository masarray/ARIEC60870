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

## Autonomous, comment-controlled stable release

The preferred maintainer flow runs from the latest merged PR on `master`:

1. Increment `Directory.Build.props` and supply a matching
   `docs/archive/release-notes/RELEASE_NOTES_vX.Y.Z.md`.
2. After the PR is merged, comment **`/release stable`** on that same PR,
   whose squash/merge commit must be the exact current master HEAD.
3. GitHub Actions verifies the commenter has write access, the version is
   newer than the latest public stable, the tag is not owned by other
   bytes, and master CI/WPF/native Windows updater FAT is proven for
   **the exact same commit**.
4. If native updater FAT has not run on that exact source, **one**
   `CI profile=updater` is dispatched. The final Linux handoff job runs
   **only** after source validation, Windows build, previous-release installer
   preparation, and genuine native FAT all succeed. It explicitly dispatches
   the trusted release controller using `workflow_dispatch` with the exact
   `release_expected_sha` (rather than depending exclusively on a
   `workflow_run` completion callback, which can be suppressed in
   `GITHUB_TOKEN` workflow chains). No repeated release comment.
5. The existing Package workflow is dispatched once with
   `expected_sha` and a global one-flight lock across all ref types,
   then builds/validates/attests/publishes the release artifacts.
6. Verify actual public ZIP/Setup/SHA-256/SBOM/provenance and sync
   README/website download links **only after publication succeeds**.

Duplicate requests and concurrent active Package or CI jobs are reused
instead of enqueueing redundant expensive Windows runners. The controller
binds each admission run's own GitHub default-branch event SHA to the exact
current `master` source: if `master` moves while a controller is queued, it
cannot dispatch or reserve any release stage for the new SHA. Use a **fresh**
`/release stable` comment on the new latest merged PR, after checking its
source version and release notes.

The continuation controller compares its own `GITHUB_SHA` and
`release_expected_sha` to current `master`, then independently locates
the latest merged PR's still-valid write-access release authorization.
While CI's tiny handoff job remains in progress, it may accept the
**already-successful source-build, installer and native FAT jobs only**;
any other running/failed CI work and pending security checks still block.
The workflow-run completion event remains an additional idempotent signal.
No new permission to publish is granted to an arbitrary workflow dispatch.

Stage reservations are short-lived Actions artifacts bound to an authorized
comment ID, source SHA and stage. They are trusted only from the official
`master` controller workflow. Uploading a reservation is **not** dispatch
proof: the owning controller must finish its `Dispatch one pinned next stage`
step successfully. A running owner keeps the lease; failed/cancelled owner
blocks instead of being treated as dispatched. Inspect the failed run and
use a fresh authorized comment after repairing the cause; do not blindly
rerun Package or reuse the old comment. Controller and Package remain
repository-wide one-flight, with exact-SHA updater/Package leases and no
unbounded retry loop.

Incomplete, failed, stale or conflicting evidence **blocks** rather than
bypasses release checks. Actual hardware interoperability is a separate test.

## GitHub Actions package flow

The repository includes:

```text
.github/workflows/release-package.yml
```

Manual release run:

1. Open **Actions**.
2. Select **Package**.
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

## Embedded Inter font acquisition

Windows builds run `pwsh ./scripts/prepare-inter-font.ps1` **before restore/build/publish**. It downloads the unmodified Inter variable TTF from the official Google Fonts repository into the ignored `Assets/Fonts/Inter.ttf` working-tree path, rejects an unexpected Git blob SHA and embeds the verified resource in the WPF executable. It is not a Windows installed-font dependency. The SIL OFL 1.1 notice is included beside the application; do not remove the notice from ZIP generation. Offline source builds require running the script while connected once; subsequent builds reuse and verify the local font.


## Windows installer (P0 integration track)

The installable EXE is generated separately by Windows CI using
`installer/ARIEC60870.iss` from the same self-contained published WPF
binary. It supports per-user in-place upgrades, a Start Menu shortcut and
preserves `%LOCALAPPDATA%\\ARIEC60870`. CI verifies a genuine published
v3.6.7 binary upgrade, a running simulated Master close, one uninstall
registration and user-data preservation. The public release workflow publishes the installer alongside the portable
ZIP and SHA256SUMS.txt. The updater PR provides explicit, operator-approved
staged installation only for managed Inno installs. Its HTTPS/GitHub checksum
validation is not a replacement for a future pinned publisher signature;
see [self-update architecture](AUTO_UPDATE_ARCHITECTURE_20261113.md).
