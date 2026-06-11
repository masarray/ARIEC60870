# IEC 60870 Standards Audit — v1.6.3

This audit documents what the application now covers and what must still be treated as roadmap. The goal is to keep the analyzer honest for field engineers: visible does not mean validated unless the engine proves the behaviour.

## IEC 60870-5-101 checklist

### Covered in current build

- FT1.2 fixed and variable frame parsing.
- Single-character ACK/NACK awareness.
- Low-baud serial profiles including 1200 bps.
- Link address size 1/2 octets for implemented unbalanced master polling.
- COT, CA, and IOA size selection.
- Type ID, VSQ, COT flags, common address, IOA, value, quality, and timestamp-oriented decode.
- ACD/DFC visibility in frame trace.
- Class 1/Class 2 unbalanced polling workflow.

### Important constraints

- Balanced IEC-101 is not implemented yet and is therefore disabled in UX.
- IEC-101 link-address size 0 is a known profile case, but it is not enabled for the current unbalanced-master workflow.
- CP24/CP32 legacy time-tag coverage should continue to be expanded with field traces.
- GI completeness needs a point-profile import before the app can prove missing/duplicate/unexpected IOA.

## IEC 60870-5-103 checklist

### Covered in current build

- FT1.2 serial relay communication.
- FUN/INF-centric evidence model and mapping profile.
- Class 1/Class 2 workflow.
- ACD/DFC visibility in frame trace.
- Protection-relay oriented event/value views.

### Open depth items

- Generic services.
- Disturbance data/file transfer.
- Vendor-specific/private ASDU ranges.
- Relay-specific interoperability templates.

## IEC 60870-5-104 checklist

### Covered in current build

- TCP client transport.
- APCI/APDU frame trace.
- I/S/U format visibility.
- STARTDT and TESTFR awareness.
- N(S)/N(R) basic visibility and basic findings.
- COT/CA/IOA profile selection.
- IEC-104 t0/t1/t2/t3/k/w settings visible and persisted.

### Open forensic items

- Full t1/t2/t3/k/w state-machine validator.
- Pending I-frame ledger and strict peer acknowledgement audit.
- STOPDT test scenario.
- Reconnect/offline-buffer behaviour validation.
- Multi-client/redundancy behaviour validation.

## Product UX audit

### Improved

- Protocol-aware setup and grids.
- No FUN/INF columns in IEC-101/104 mode.
- Value and quality are separated.
- IED/RTU timestamp is preserved into UI rows.
- ACD/DFC is visible for serial forensic trace.
- Last setup is persisted so users do not retype field parameters repeatedly.

### Remaining UX proof work

- Profile health meter: show which interoperability fields are complete/missing.
- IOA profile import and GI completeness matrix.
- Behaviour validation tab with PASS/FAIL per scenario.
- Forensic package export with raw stream, profile snapshot, hashes, and session manifest.
