# ARIEC60870 AutoTest Assessment

ARIEC60870 v1.0 adds a lightweight FAT/SAT-style assessment layer on top of the active IEC-103 master evidence.

The assessment does not replace raw frame evidence. It summarizes the evidence into checks that are easier to review after a relay communication test.

## Assessment areas

- Session completion
- Bidirectional communication activity
- FT1.2 frame quality
- General Interrogation completion
- SCADA-style polling policy
- Response timeout behavior
- Relay value/status acquisition
- Relay timestamp quality for Event Log
- User mapping profile coverage
- Error/warning findings

## Status meanings

| Status | Meaning |
|---|---|
| Pass | Evidence is healthy for the current check. |
| Warning | Test can continue, but the result needs engineering review. |
| Fail | The session contains a failure condition and should not be accepted as FAT/SAT evidence without correction. |
| Info | Informational item; not scored as pass/fail. |

## Product rules enforced by assessment

1. Class 2 is the normal background polling cycle.
2. Class 1 is an event-drain path, triggered by ACD=1 or bounded GI follow-up.
3. Continuous Class 1 polling while the relay returns NO DATA is not acceptable.
4. Event Log uses relay timestamp from IEC-103 ASDU time fields, not PC arrival time.
5. Signal names only come from user mapping profile; raw FUN/INF remains the source of truth.

## Report output

The Markdown evidence report includes an **AutoTest assessment** section with overall status, score, evidence, and recommendation per check.

The CLI also prints the assessment checklist after each active master run.
