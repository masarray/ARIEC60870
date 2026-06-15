# Testing Strategy

ARIEC60870 is an evidence-oriented protocol tool, so the test strategy prioritizes deterministic protocol behavior, report stability, and public repository guardrails.

## Test layers

### 1. Protocol smoke tests

`tests/ARIEC60870.Protocol.Tests` is a dependency-free console test runner. It validates sanitized FT1.2 and IEC-101/104 vectors without relying on a unit-test framework. This keeps a simple fallback that can be used during field debugging or restricted environments.

### 2. xUnit regression tests

The xUnit projects provide first-class CI evidence:

- `ARIEC60870.Core.Tests` checks the FT1.2 parser, IEC-103 ASDU decoder, trace extraction, and analyzer findings.
- `ARIEC60870.Master.Tests` checks IEC-101/104 ASDU construction/decoding, IEC-104 APDU parsing, command summaries, report-safe settings snapshots, and AutoTest assessment policy.
- `ARIEC60870.Reporting.Tests` checks Markdown report sections, privacy sanitization, table escaping, diagnostic appendix output, and event row limiting.
- `ARIEC60870.Desktop.Tests` checks the `.ariec` capture row contract without starting the WPF UI.
- `ARIEC60870.Repository.Tests` checks public repository hygiene, CI posture, documentation links, release workflow posture, and Phase B architecture guardrails.

### 3. Manual validation matrix

`docs/VALIDATION_MATRIX.md` records simulator, package, and relay validation status. Automated tests prove deterministic behavior; the validation matrix records package and device-level evidence that cannot be fully simulated inside CI.

## CI evidence

The CI workflow publishes:

- protocol smoke-test log;
- `.trx` test result files for every xUnit suite;
- XPlat Code Coverage collector output;
- a single downloadable `ARIEC60870-test-results` artifact.

## Release expectation

A release should not be promoted from beta to stable until:

1. source build passes;
2. dependency-free protocol smoke tests pass;
3. all xUnit regression suites pass;
4. portable/single-file package verification passes;
5. at least one simulator/package run is recorded in the validation matrix;
6. any real-device result is sanitized before being added to public docs.

## Regression areas to protect

High-priority regression areas are:

- FT1.2 frame parsing, checksum, ACK/NACK, and link-address width handling;
- IEC-103 Type 1 / Type 5 / Type 8 / Type 9 / private ASDU behavior;
- IEC-101/104 Type ID, COT, CA, IOA, SQ, quality, and command encoding;
- IEC-104 I/S/U APDU parser behavior;
- GI lifecycle evidence and missing-GI-END findings;
- report privacy and Markdown table escaping;
- `.ariec` capture row JSON compatibility;
- repository release workflow and architecture guardrails.


## Report PDF regression

`ARIEC60870.Desktop.Tests` includes a native PDF evidence report smoke test. It verifies that the desktop report engine can generate a real `%PDF` file from sanitized evidence rows, preventing regressions back to HTML-print report export.
