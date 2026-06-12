# ARIEC60870 v3.2.0 — GI Completeness Matrix + IOA Coverage Proof

## Added

### GI / scan completeness matrix

The protocol proof layer now calculates expected vs observed coverage from the loaded Signal List / IOA database.

New proof diagnostics:

- `ARIEC-PROOF-GI-COMPLETENESS-PASS`
- `ARIEC-PROOF-GI-COMPLETENESS-RISK`
- `ARIEC-PROOF-DIGITAL-COVERAGE-PASS`
- `ARIEC-PROOF-DIGITAL-COVERAGE-RISK`
- `ARIEC-PROOF-ANALOG-COVERAGE-PASS`
- `ARIEC-PROOF-ANALOG-COVERAGE-RISK`
- `ARIEC-PROOF-COMMAND-MAPPING-PASS`
- `ARIEC-PROOF-COMMAND-MAPPING-RISK`
- `ARIEC-PROOF-MAPPING-COVERAGE`

### Coverage categories

The matrix tracks:

- all mapped monitor points,
- digital SP/DP monitor points,
- analog measurement points,
- other monitor points,
- command points,
- command points with feedback IOA mapping,
- missing monitor preview.

### Export integration

Evidence retention/export policy now includes:

- GI coverage matrix,
- digital coverage,
- analog coverage,
- command feedback mapping coverage,
- missing IOA preview.

## Why

v3.1.0 could say that data was observed. v3.2.0 can now say how complete that proof is against the expected IOA database.
