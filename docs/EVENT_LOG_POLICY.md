# Relay Event Log Policy

The Relay Event Log is an SOE-style engineering list. It is not a raw frame trace.

## Event Time

Event Log time must use the **relay timestamp** from IEC-103 ASDU time fields when available.

PC arrival time must be stored only as forensic metadata.

Reason:

- relay timestamp represents when the relay says the event happened
- PC arrival time represents when the tester received the frame
- mixing the two makes event chronology misleading

## What Enters Event Log

Allowed:

- state change / edge event from relay
- spontaneous event
- time-tagged DPI/SPI event
- protection/status event with relay timestamp
- mapped or unmapped event whose state differs from the last known state

Not allowed by default:

- every repeated GI snapshot
- every repeated cyclic/background status with no state change
- every raw NO DATA response
- every ACK/control response

Those belong in Evidence / Frame Monitor, not Relay Event Log.

## Value Viewer vs Event Log

```text
Value Viewer:
  latest known state/value snapshot
  updated by GI/status/cyclic/event frames

Relay Event Log:
  chronological edge/state-change history
  uses relay timestamp
```

## Mapping Behavior

If user mapping exists:

```text
Breaker Position | Closed | RelayTime | FUN/INF raw retained
```

If no mapping exists:

```text
FUN 192 / INF 36 | DPI=2 | RelayTime | raw retained
```

Never hide raw FUN/INF.

## Missing or Invalid Relay Timestamp

If ASDU timestamp is missing or invalid:

- keep entry if it is a real state change/event
- display `Relay timestamp unavailable/invalid`
- use PC arrival time only in a secondary metadata column
- create a warning if timestamp quality matters for the session

## GI Behavior

GI often returns a snapshot. Snapshot frames should update Value Viewer.

They should enter Relay Event Log only when:

- the value represents a state change compared with previous known state
- the frame is explicitly event-like/spontaneous
- user enables a debug option to log GI snapshots
