# Forensic Audit v1.6.5

## Audit position

The application is now a credible ARIEC60870 protocol lab baseline, but it should still be described as a **protocol analyzer with forensic-oriented evidence**, not a final certified forensic validator.

The next maturity jump is not more raw columns. The next jump is explicit proof:

- what was requested,
- what the slave/server answered,
- whether timing and sequence rules were respected,
- whether GI returned all expected objects,
- whether commands followed the correct lifecycle,
- whether the exported evidence can be trusted and replayed.

## IEC-101 gaps

### 1. Balanced mode is still not implemented

The UI is honest now: Balanced is planned, not active. Keep it that way until a separate balanced-session engine exists.

Current engine remains an unbalanced master polling implementation.

### 2. Link address size 0 is recognized but not active

This is acceptable for the current unbalanced master workflow. It should only become selectable when balanced or monitor mode is implemented.

### 3. GI completeness needs an IOA profile

GI drain is now safer, but the app still cannot prove that all expected IOAs were returned. It can prove that GI data was observed; it cannot yet prove completeness.

Required next model:

```text
CA, IOA, SignalName, ExpectedTypeId, ExpectedGIGroup, Class, Unit, Scale, ExpectedQualityPolicy
```

### 4. CP24/CP32/CP56 coverage needs test-vector expansion

CP24 and CP56 are present, but the decoder should be backed by more deterministic vector tests for every time-tagged Type ID used by legacy RTUs.

### 5. Command lifecycle validation is missing

Needed for FAT/SAT:

- C_SC_NA_1
- C_DC_NA_1
- C_RC_NA_1
- C_SE_NA_1 / C_SE_NB_1 / C_SE_NC_1
- direct operate
- select-before-operate
- ACTCON
- ACTTERM
- negative confirmation
- wrong CA
- unknown IOA
- timeout and busy response

## IEC-104 gaps

### 1. State-machine enforcement is still partial

The app decodes I/S/U, STARTDT, TESTFR, N(S) and N(R), but the validator is not yet strict enough.

Required next engine:

```text
Iec104StateMachineValidator
- pending I-frame ledger
- t1 timeout for sent/test APDUs
- t2 delayed S-frame acknowledgement
- t3 idle TESTFR supervision
- k outstanding I-format APDU window
- w receiver acknowledgement threshold
- STARTDT before I-frame enforcement
- STOPDT behavior
- duplicate/stale/unexpected N(R) findings
```

### 2. Redundant connection behaviour is not validated

Useful future checks:

- two TCP clients / duplicate client policy
- STARTDT active vs standby connection
- STOPDT ACT/CON path
- reconnect and queue behavior
- spontaneous buffer after reconnect

### 3. APDU length and ASDU length limits should be reported

Add explicit findings for APDU length, ASDU length, truncated APDU and unsupported oversized payload.

## IEC-103 gaps

### 1. Generic services are not fully covered

IEC-103 is not just FUN/INF basic events. For relay forensic use, generic services and private ranges need a dedicated decoder/audit tab.

### 2. Disturbance/file transfer workflow is not implemented

Relay forensic often needs disturbance list and file-transfer evidence. Add at least discovery/reporting of ASDU 23..31 behavior and vendor interoperability flags.

### 3. Vendor interoperability templates are missing

Do not guess vendor signal databases, but allow user-loaded templates:

```text
Vendor, RelayFamily, FUN, INF, Description, EventType, ExpectedClass, TimeTagPolicy
```

## UX gaps

### 1. Profile health should be visible

Add a card:

```text
Profile health: 62%
Missing: IOA profile, expected TypeId list, command policy, IEC-104 state validator
```

### 2. Findings should be grouped by proof domain

Current findings are useful but should be grouped into:

- Frame integrity
- Link-layer behavior
- ASDU decode
- GI completeness
- Command behavior
- IEC-104 state machine
- Timing/SOE
- Profile mismatch
- Evidence integrity

### 3. Engineer-readable object grid

For 101/104, add an ASDU Objects grid separate from frame trace:

```text
Frame # | CA | IOA | Type ID | COT | Value | Quality | IED/RTU time | Raw element
```

## Evidence package gaps

For forensic-grade output, add:

```text
Session manifest
App version and build hash
Profile snapshot and hash
Raw binary stream
Frame offset
Per-frame SHA256
Whole-session SHA256
PC wall-clock timestamp
PC monotonic timestamp
Timezone
Operator/device/project metadata
```

## Recommended next version

v1.7.0 should be an **IEC-104 State Machine + IOA Profile Foundation** release.

Priority order:

1. IOA point profile model and CSV import.
2. GI completeness matrix.
3. IEC-104 state-machine validator.
4. Findings domain grouping.
5. ASDU Objects grid.
