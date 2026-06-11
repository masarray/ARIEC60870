# ARIEC60870 v1.3.0 — Field Validation & Download Experience

ARIEC60870 v1.3.0 improves the public download experience and adds stronger validation guardrails for users who want to try the IEC-103 master tester on a bench, simulator, or relay communication setup.

## Highlights

- README now includes application screenshots so new users can see the desktop workflow before downloading.
- Landing page now explains that ARIEC60870 is free, open source, and available without a license key or subscription.
- Added a clearer download-and-try flow for Windows users.
- Added sanitized IEC-103 FT1.2 / ASDU test vectors for release validation.
- Expanded protocol smoke tests to cover fixed frames, Type 1 event messages, Type 5 identification, Type 8 GI end, Type 9 measurands, and private/unknown ASDU transparency.
- Updated validation documentation so users can record simulator, bench, and relay checks.

## What is included in the portable package

- Windows desktop IEC-103 active master tester.
- CLI tools for active master runs, offline trace analysis, and simulator checks.
- Sample mapping profile.
- Sanitized test vectors for protocol smoke tests.
- Quick Start and Troubleshooting guides.
- License, notices, and checksum file.

## How to try it

1. Download `ARIEC60870-v1.3.0-win-x64-portable.zip` from the release assets.
2. Extract the ZIP to a local folder.
3. Run `Start-ARIEC60870.bat`.
4. Configure COM port, baudrate, link address, common address, timeout, GI, and optional mapping profile.
5. Click **Start** and review Operator Evidence, Value Viewer, Relay Event Log, Frame Trace, and Diagnostics.
6. Export evidence after the session.

## Evaluation note

This release is suitable for test-bench evaluation, simulator checks, IEC-103 troubleshooting review, and public feedback.

For contractual FAT/SAT use, validate the package with the target relay, project serial settings, and approved mapping profile before relying on exported evidence.
