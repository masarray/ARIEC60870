# ARIEC60870 v1.2.31 - Windows Portable Release Flow

This release improves how users get and verify the Windows portable package from GitHub Releases.

## What is included

- Windows portable ZIP package for running the desktop app without opening the source project.
- `SHA256SUMS.txt` for verifying downloaded release assets.
- Quick Start guide for first relay communication checks.
- Troubleshooting guide for common IEC-103 serial symptoms.
- Validation matrix template for recording simulator, bench, and relay checks.
- Cleaner README and landing-page links for users who want to download, run, and evaluate the application.

## Release assets

```text
ARIEC60870-v1.2.31-win-x64-portable.zip
SHA256SUMS.txt
```

## How to try it

1. Download `ARIEC60870-v1.2.31-win-x64-portable.zip` from GitHub Releases.
2. Extract the ZIP to a local folder.
3. Run `Start-ARIEC60870.bat`.
4. Open **Setup** and configure the relay serial settings.
5. Click **Start** and review Operator Evidence, Value Viewer, Relay Event Log, Frame Trace, and Diagnostics.
6. Export evidence after the test session when needed.

## Notes for evaluation

This build is intended for test-bench evaluation, communication troubleshooting, and protocol evidence review. Validate the application with the target relay, serial settings, and project mapping profile before relying on exported evidence for formal FAT/SAT records.
