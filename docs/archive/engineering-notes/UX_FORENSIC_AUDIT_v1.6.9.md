# UX and Forensic Audit v1.6.9

## Finding 1 — Operator Evidence was too column-heavy

The previous Operator Evidence grid showed too many protocol fields at once: Type ID, COT, CA, IOA, FUN/INF, APCI, quality, timestamp, meaning, action, and other technical columns. This made the tab hard to read and weakened the operator workflow.

### Decision

Operator Evidence is now a summary view. Deep protocol fields are moved to Frame Trace and the row inspector.

### Default Operator Evidence columns

- Time
- Direction
- Service
- Address
- Signal
- Value
- Quality
- IED/RTU time
- Meaning

## Finding 2 — IEC-101 Before state could be empty

The Event Log relied on `PreviousSignalValue` from the runtime event. IEC-101/104 process-value evidence can arrive without that field populated even when the current value is valid.

### Decision

The desktop now maintains a last-value cache keyed by protocol + CA + IOA. Event Log `Before` uses that cache if the runtime event does not provide an explicit previous value.

## Finding 3 — Value/Event grids needed operator-first readability

The Value Viewer and Event Log were not as crowded as Operator Evidence, but they still exposed too many raw technical columns by default.

### Decision

Value/Event grids now show one compact Address column while raw CA/IOA/FUN/INF/Type ID fields remain available in Frame Trace.

## Remaining high-impact gaps

- IEC-104 full state-machine validator.
- GI completeness matrix using loaded IOA profile.
- Command Behaviour Validation Studio with ACTCON/ACTTERM/negative confirmation and timing verdicts.
- Slave simulator project for closed-loop master/slave validation.
- Immutable forensic package with raw stream, frame hash, profile snapshot hash, and session manifest.
