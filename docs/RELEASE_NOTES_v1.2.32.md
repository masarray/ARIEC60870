# ARIEC60870 v1.2.32 - User-Facing Release Wording

This release refreshes public-facing wording across the README, landing page, release notes, and release documentation so the repository reads like a download-ready engineering tool, not an internal project checklist.

## What changed for users

- README now starts with download, run, and first-use instructions.
- Landing page now explains package contents and usage flow instead of maintainer release mechanics.
- Release notes now describe what users receive in the ZIP and how to try it.
- GitHub release fallback notes are now user-facing when a version-specific release note file is missing.
- Planned improvements are written as public product direction, not internal maintainer advice.

## Release assets

```text
ARIEC60870-v1.2.32-win-x64-portable.zip
SHA256SUMS.txt
```

## How to try it

1. Download the Windows portable ZIP from GitHub Releases.
2. Extract the package.
3. Run `Start-ARIEC60870.bat`.
4. Configure COM port, baudrate, parity, link address, common address, timeout, GI option, and optional mapping profile.
5. Start the session and review the evidence screens.
6. Export evidence when the test result needs to be shared or archived.

## Notes for evaluation

Use this build for IEC-103 relay communication checks, bench testing, troubleshooting, and evidence review. For formal FAT/SAT use, validate it with the target relay and approved project mapping profile first.
