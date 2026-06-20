# Master Polling Policy

ARIEC60870 must behave like a disciplined SCADA master, not like a noisy Class 1 poll loop.

## Core Rule

```text
Class 2 = normal/background polling
Class 1 = pending/event data drain
```

Class 1 should be requested aggressively only when there is a reason:

- ACD=1 in a secondary response
- bounded follow-up after a GI command
- controlled recovery/test step where the reason is recorded as evidence

## Forbidden Behavior

```text
Master -> Request Class 1
Slave  -> NO DATA / ACD=0
Master -> Request Class 1
Slave  -> NO DATA / ACD=0
Master -> Request Class 1
```

This is noisy, low-value traffic. It hides real events, fills traces, and is not a good field testing behavior.

## Startup Sequence

Recommended default:

```text
Open transport
Optional startup delay
Optional Reset Remote Link
Reset FCB
Optional Clock Sync
Optional GI
Bounded GI Class 1 follow-up
Enter normal Class 2 cycle
```

If IEC-101 station GI (`C_IC_NA_1`, QOI=20) is negatively confirmed, the master must record the negative confirmation as evidence and avoid waiting forever for a station-GI image that the outstation rejected. A bounded group interrogation fallback (QOI=21..36) is allowed on the active link, followed by the same bounded Class 1 drain and normal Class 2 verification sweep. Standby links must not perform this fallback because it can drain application queues.

## Normal Runtime

```text
Request Class 2 at configured interval
If response ACD=0:
  continue Class 2 cycle

If response ACD=1:
  enter Class1EventDrain
```

## Class 1 Event Drain

```text
Request Class 1
Decode response
Update Value Viewer / Event Log / evidence
Continue only while event queue appears active
Stop when:
  NO DATA
  ACD clear
  GI END received during GI drain
  DFC busy
  timeout
  max drain count reached
```

## DFC Handling

If DFC=1:

- record finding/evidence: slave busy / data flow control active
- back off before retry
- do not increase Class 1 pressure

## Timeout Handling

If no response:

- record timeout evidence
- retry according to configured policy
- use Reset FCB only after configured timeout burst
- never convert timeout into unbounded Class 1 flood

## Evidence Required

Every master request must carry a reason:

```text
NormalClass2Cycle
AcdIndicatedClass1Drain
GiFollowUp
ManualCommand
TimeoutRecovery
```

The report must show why Class 1 was requested.
