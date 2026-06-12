# ARIEC60870 v2.1.0 — Scan Health + Command Behaviour Validator

## Added

### Lightweight Scan Health Engine

The desktop runtime now observes IEC-101/104 scan behaviour and raises rate-limited diagnostics when the session shows field-risk symptoms:

- `IEC101-SCAN-DFC-BUSY`
  - Outstation reports DFC=1 / busy.
- `IEC101-SCAN-SLOW-RESPONSE`
  - Response time is high for practical polling.
- `IEC10X-SCAN-PROCESS-STARVATION`
  - No process IOA update arrives for a suspicious interval while the session is still running.
- `IEC101-CLASS2-STARVATION`
  - Class 2/background scan appears stale.
- `IEC101-ACD-STUCK-HIGH`
  - ACD remains high for too long and Class 1 pending state is not clearing.

The diagnostics are rate-limited so the UI stays light.

### Command Behaviour Validator

The runtime now creates a lightweight command ledger for IEC-101/104 command Type IDs 45..51:

- records command TX,
- watches ACTCON,
- watches ACTTERM,
- watches negative confirmation,
- checks mapped feedback IOA from the Signal List profile,
- produces timeout verdict when command proof is incomplete.

New diagnostic codes:

- `IEC10X-COMMAND-TX`
- `IEC10X-COMMAND-ACTCON`
- `IEC10X-COMMAND-ACTTERM`
- `IEC10X-COMMAND-FEEDBACK-PROVEN`
- `IEC10X-COMMAND-NEGATIVE-CONFIRMATION`
- `IEC10X-COMMAND-VERDICT-TIMEOUT`

## Design

This phase deliberately avoids adding heavy UI grids. Scan health and command validation are delivered as diagnostics/session evidence, while Value Viewer and Protocol Trace remain the live views.
