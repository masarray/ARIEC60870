# Release Notes - v1.2.27 Protocol Retry Safety

This release improves public-release readiness for ARIEC60870 as an IEC-103 active master tester.

## Fixed

- FCB is no longer toggled immediately after sending an FCV command.
- FCB now advances only after a valid secondary relay response or single-character ACK.
- Timeout, malformed frame, checksum error, or ambiguous primary-looking response keeps the previous FCB value for safer retry behavior.
- FT1.2 stream reader now resynchronizes through serial noise bytes until a valid FT1.2 start byte is found.

## Added

- Dependency-free protocol smoke test project under `tests/ARIEC60870.Protocol.Tests`.
- CI workflow for restore, build, and protocol smoke tests.
- Tests covering parser validation, stream resync, and FCB retry behavior.

## Evidence privacy

- Master evidence report settings now use a report-safe snapshot.
- Mapping profile export defaults to file name only instead of full local workstation path.
- Offline analyzer report source file now uses file name only.

## Documentation

- README refreshed for user-facing feature and usage clarity.
- Landing page copy refreshed to focus on what users need: features, workflow, polling behavior, evidence model, and how to use the app.
- Public wording avoids unnecessary implementation/library references and avoids overclaiming compliance.
## Landing page cleanup

- Removed the incorrect landing screenshot asset and its landing-page card.
- Removed the matching raw source screenshot from the landing screenshot folder.
- Refined landing copy so it stays focused on user features, workflow, evidence, and how to use the software.

