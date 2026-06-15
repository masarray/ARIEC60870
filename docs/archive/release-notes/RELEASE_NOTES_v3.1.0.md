# ARIEC60870 v3.1.0 — Protocol Proof Layer + Session Verdict Ledger

## Added

### Protocol proof markers

The UI now emits explicit proof diagnostics for IEC-101/104 runtime evidence:

- `ARIEC-PROOF-CA-OBSERVED`
- `ARIEC-PROOF-GI-SEEN`
- `ARIEC-PROOF-GI-COMPLETE`
- `ARIEC-PROOF-GI-NEGATIVE`
- `ARIEC-PROOF-DIGITAL-DATA`
- `ARIEC-PROOF-ANALOG-DATA`
- `ARIEC-PROOF-COMMAND-TX`
- `ARIEC-PROOF-SESSION-VERDICT`

### Session proof verdict

When a completed result is applied, the desktop app generates a session-level protocol proof verdict.

The verdict summarizes:

- observed ASDU CA,
- GI activity,
- GI completion/negative confirmation,
- digital SP/DP observation,
- analog measurement observation,
- command/feedback proof state,
- GI completeness,
- retention/backpressure state,
- dispatcher risk.

### Export proof state

Evidence retention/export policy now includes protocol proof state:

- CA observed,
- GI seen/completed/negative,
- digital/analog data proof,
- command/feedback proof.

## Why

After the runtime UI engine became bounded and trace retention became auditable, the next step is protocol credibility: the app should explicitly state what has been proven, what remains unproven, and what risks are open.
