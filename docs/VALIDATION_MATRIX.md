# ARIEC60870 Validation Matrix

This document records simulator, bench, and relay checks for ARIEC60870 releases. It helps users understand which scenarios have been exercised before using a package in their own environment.

## Release validation status

| Item | Status | Notes |
|---|---|---|
| Build on Windows | CI passed (2026-10-30) | Exact master a9e5cd6 Windows Release build, 0 warnings/errors; [run 41026302020](https://github.com/masarray/ARIEC60870/actions/runs/41026302020). Clean-machine field install remains pending. |
| Protocol smoke tests | Available | Run `dotnet run --project tests/ARIEC60870.Protocol.Tests`. |
| xUnit regression suites | Available | Core, Master, Reporting, Desktop, and Repository test projects publish `.trx` and coverage output in CI. |
| Portable package created | CI passed (2026-10-30) | Win-x64 artifact available for master a9e5cd6 in [run 41026302020](https://github.com/masarray/ARIEC60870/actions/runs/41026302020). Physical Windows installation test is separate. |
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
| VAL-001B | xUnit regression suite | Local/CI | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A | TRX + coverage | Available | Run `dotnet test` for Core, Master, Reporting, Desktop, and Repository test projects. |
| VAL-002 | Built-in simulator | Local/simulated | N/A | 1 / 1 | N/A | Pass | Pass | Pass | N/A | Example | Pass | Pending | Run for package smoke check. |
| VAL-003 | IEC-103 relay A | RS-485 | 9600 / Even | 1 / 1 | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Sanitized bench or field test. |
| VAL-004 | IEC-103 relay B | RS-485 | 19200 / Even | 1 / 1 | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Sanitized bench or field test. |
| VAL-005 | IEC-101 legacy RTU / utility-style low-rate channel | RS-232/RS-485 | 1200 / Even | Project-specific | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Pending | Validate low baudrate timing, timeout, and Class 1/Class 2 polling behaviour. |

## Public beta baseline

A public beta package is expected to have:

- source build result available;
- protocol smoke test result available;
- xUnit regression test result and coverage artifact available;
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


| Report PDF generation | `ARIEC60870.Desktop.Tests` | Verifies direct native PDF output and guards against legacy conversion report regressions. |

## Field readiness milestone (2026-10-31)

| Gate | State | Evidence and limits |
| --- | --- | --- |
| Source-validated 2/4-COM preflight | Implemented, automated regression pending CI | `PassiveFieldCapturePlan` checks one/two paired circuits, distinct COM inputs, 1200-baud framing, duration and IEC interpretation widths without COM open |
| Windows portable RX-only field utility | Implemented, CI gate pending | A separate self-contained portable artifact with help, Windows COM inventory, absent-port fail-closed smoke, raw journal capture, final SHA/CRC verifier and human-readable field instructions |
| Full 2/4 RX real device traffic | **Not yet tested** | Requires electrically approved isolated tap, Windows adapter inventory, valid original frames and source mapping |
| 1200-baud stop/reopen and multi-hour 2/4-RX soak | **Not yet tested** | Real USB serial adapters, physical wiring, independent clock/timing assessment and field-sanitized evidence |
| IEC-101/103/104 active device FAT | **Not yet proven by field toolkit** | Separate active bench setup with authorized transmission, link verification, GI and Class1/2/commands tested on isolated equipment |

The headless capture package is complementary to the main WPF GUI. Its `field-summary.json` is advisory; source-specific raw journals, manifest and the independent verifier are authoritative for **application journal consistency only**. Do not conflate that with completeness of bytes delivered by an RS232/RS485 converter.

## Decoder truth matrix — reserved/unsupported IEC-101/104 Type IDs (2026-11-17)

This is **source-level synthetic regression coverage**, not a device or interoperability FAT. The Core decoder must never invent information-object boundaries when an IEC-101/104 Type ID has no supported, fixed information-element length. A valid ASDU header remains available, and the full original ASDU including opaque payload remains `RawBytes`; objects, inferred SQ=1 IOAs, quality and values stay empty/untrusted. This is distinct from standard supported types and from vendor-private Type IDs 128–255.

| Fixture | Type / profile | Required result | Gate |
| --- | --- | --- | --- |
| Reserved type, SQ=0 and SQ=1 | TI17, COT1, CA1, IOA widths 1/2/3 | Exact original bytes, verified header/COT/CA, zero fabricated objects or IOAs, explicit unsupported issue | Master xUnit `UnsupportedTypeRetainsExactWireBytesWithoutGuessingObjectLayout` |
| Vendor-private opaque payload | TI200, COT1, CA1, IOA3 | Exact original bytes and no fabricated object or false corruption warning | Master xUnit `VendorPrivateTypeRemainsOpaqueWithoutSpuriousCorruption` |
| Valid SQ1 boundary | TI1, IOA widths 1/2/3 | Two correctly derived addresses with second wire offset absent | Master xUnit `KnownSq1SequenceDecodesOnlyWithinConfiguredIoaWidth` |
| SQ1 address overflow | TI1, IOA widths 1/2/3 | First valid object retained, overflowed object not promoted, explicit decode issue, original bytes unchanged | Same xUnit |
| Standard normalized without quality | TI21, COT1, CA1, IOA3 | Typed normalized value; no invented QDS, original bytes unchanged | Master xUnit `NormalizedWithoutQdsHasNoInventedQualityOrTrailingObject` |

This matrix does **not** cover every IEC-10x Type ID or certify interpretation of IEC-103 relay/private payloads. Existing GUI Trace/HEX/replay/export/report parity and high-rate/1200-baud physical tests remain separate acceptance items under issue #120. Do not update a public release's advertised decoder coverage before exact-head CI and trace-surface inspection pass.
