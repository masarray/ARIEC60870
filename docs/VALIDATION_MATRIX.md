# ARIEC60870 Validation Matrix

This document records simulator, bench, and relay checks for ARIEC60870 releases. It helps users understand which scenarios have been exercised before using a package in their own environment.

## Release validation status

| Item | Status | Notes |
|---|---|---|
| Build on Windows | Pending | GitHub Actions or local build result. |
| Protocol smoke tests | Available | Run `dotnet run --project tests/ARIEC60870.Protocol.Tests`. |
| Portable package created | Pending | Windows portable ZIP generated. |
| Portable package opened | Pending | Desktop app starts from extracted ZIP. |
| CLI help opened | Pending | CLI tools open from extracted ZIP. |
| Simulator master run | Pending | Deterministic simulator mode produces evidence. |
| Sanitized test vectors | Available | FT1.2 / ASDU examples are included in `samples/test-vectors/`. |
| Real relay validation | Pending | Sanitized relay results can be added below. |

## Device validation table

For public repositories, use sanitized device names when project, customer, or vendor details cannot be shared.

| Test ID | Device / Simulator | Connection | Baud / Parity | Link / CA | Reset Link | GI | Class 2 | Class 1 / ACD | Time Sync | Mapping | Evidence Export | Result | Notes |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| VAL-001 | Protocol test vectors | Local/offline | N/A | 1 / 1 | N/A | N/A | N/A | N/A | N/A | Example | N/A | Available | Run `dotnet run --project tests/ARIEC60870.Protocol.Tests`. |
| VAL-002 | Built-in simulator | Local/simulated | N/A | 1 / 1 | N/A | Pass | Pass | Pass | N/A | Example | Pass | Pending | Run for package smoke check. |
| VAL-003 | IEC-103 relay A | RS-485 | 9600 / Even | 1 / 1 | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Sanitized bench or field test. |
| VAL-004 | IEC-103 relay B | RS-485 | 19200 / Even | 1 / 1 | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Sanitized bench or field test. |
| VAL-005 | IEC-101 legacy RTU / PLN-style low-rate channel | RS-232/RS-485 | 1200 / Even | Project-specific | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Validate low baudrate timing, timeout, and Class 1/Class 2 polling behaviour. |

## Public beta baseline

A public beta package is expected to have:

- source build result available;
- protocol smoke test result available;
- portable package generated;
- desktop app opened from the portable package;
- simulator mode evidence generated;
- release notes that describe maturity honestly.

## Stable release baseline

A stable release is expected to have:

- at least two independent relay/simulator validations;
- GI behavior observed and documented;
- Class 2 polling stable for a defined duration;
- Class 1 drain behavior verified with at least one event source;
- timeout/checksum/malformed recovery reviewed;
- exported evidence reviewed for privacy and clarity.
