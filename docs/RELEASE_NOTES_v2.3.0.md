# ARIEC60870 v2.3.0 — Command UX Safety Preview

## Added

### Command preview card

The Command Dock now shows a command preview before SELECT/OPERATE:

- selected command signal name,
- command type,
- CA,
- command IOA,
- command policy,
- feedback IOA,
- feedback signal name when available,
- safety/validator guidance.

### Command issue guard

The app now validates selected command signal against manual CA/IOA before issuing a command:

- `IEC10X-COMMAND-TARGET-MISMATCH`
  - selected command signal IOA and IOA box differ.
- `IEC10X-COMMAND-CA-MISMATCH`
  - selected command signal CA and CA box differ.
- `IEC10X-COMMAND-NO-FEEDBACK-MAP`
  - command is allowed, but no feedback IOA is mapped.

Manual IOA entry remains allowed. If the user intentionally wants manual testing, they can type CA/IOA without selecting a command signal.

## Why

Command execution must be safer than raw IOA typing. If the database knows the command point and feedback IOA, the UI should expose that before the operator clicks SELECT/OPERATE.
