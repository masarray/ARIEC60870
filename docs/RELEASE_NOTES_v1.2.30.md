# ARIEC60870 v1.2.30 - Release Packaging Hardening

This release focuses on making ARIEC60870 easier to publish and easier to try from GitHub Releases.

## Highlights

- Added Windows portable package script.
- Added release package verification script.
- Added GitHub Actions workflow for portable ZIP and SHA256 checksum artifact.
- Added release packaging guide.
- Added quick-start guide for first relay communication checks.
- Added practical troubleshooting guide.
- Added validation matrix template for relay and simulator testing.
- Updated README and landing page with clearer release workflow entry points.

## Recommended release assets

```text
ARIEC60870-v1.2.30-win-x64-portable.zip
SHA256SUMS.txt
```

## Validation recommendation

This version remains suitable as a public beta / release candidate until enough real relay or independent simulator validation is recorded in `docs/VALIDATION_MATRIX.md`.
