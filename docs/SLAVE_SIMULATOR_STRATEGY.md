# ARIEC60870 Slave Simulator Strategy

## Why the simulator is needed

ARIEC60870 is now a multi-protocol IEC 60870 lab, not an IEC-103-only tester. The master/client analyzer can only become forensic-proof when it has a controlled slave/server counterpart for repeatable closed-loop validation.

The planned WPF slave simulator belongs in the same solution and should cover:

```text
IEC-101 controlled station / serial slave
IEC-103 relay-style slave
IEC-104 controlled station / TCP server
```

The simulator must reuse the same shared protocol core, profile database, ASDU builder/decoder, timestamp encoder/decoder, and type catalog used by the master analyzer.

## Product boundary

ARIEC60870 product direction:

```text
ARIEC60870 Protocol Lab
├─ Master Analyzer / Client Tester
├─ Slave Simulator / Controlled Station Server
├─ PLN PUSERTIF profile seed
└─ Forensic evidence and behaviour validation
```

The immediate implementation order should be:

1. IEC-104 WPF server simulator for localhost closed-loop validation.
2. IEC-101 serial controlled-station simulator with Class 1/Class 2 queue and ACD/DFC behaviour.
3. Command behaviour validation: double command, regulating command, setpoint command, ACTCON, ACTTERM, negative confirmation.
4. IEC-103 relay-style slave for FUN/INF regression and protection-event demo.

## Runtime signal database

The simulator should expose a runtime-editable signal grid with:

```text
Enabled | CA | IOA/FUN-INF | Name | Type ID | Class | Value | Quality | Timestamp | Unit | Command policy
```

When the user changes a value, the simulator should update timestamp/quality, queue Class 1 events for IEC-101/103, and push spontaneous I-format data for IEC-104 when STARTDT is active.

## Database editor

The simulator must include a database editor for add/delete/edit/import/export of IOA mapping profiles. The default seed should be the PLN PUSERTIF form-based profile, while users can edit or replace the database for global projects.
