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

## M2 — received spontaneous event vs state transition (2026-10-29)

- For valid RX IEC-101/104 digital ASDU, COT 3 (spontaneous) and 11/12 (return information) each represent an **observed protocol event**, even when the reported state repeats or was not known previously. The event reason identifies `first observation`, `repeat observation`, or `state change`; these are not synonymous.
- GI/cyclic digital snapshots enter SOE only with a known state transition. Repeated unchanged GI values must not create synthetic events.
- IEC-101/104 Values identity is protocol + physical channel + Common Address + IOA, **not IOA alone**. Mapping requires compatible CA/Type or an explicit wildcard; mismatch stays visible and diagnostic.
- GI completion tracks configured CA+IOA independently of physical channel; other CA must not satisfy expected coverage.
- Original RX tap stream/byte offset and object index remain available as a capture-local source reference. Derived references never imply verified raw integrity on their own.
- Refer to `docs/IEC10X_M2_PHYSICAL_IDENTITY_SOE.md` for limitations and tests.

## Operator Event timeline UX (2026-10-29)

Events must show **one Value column** rather than separate Before/After columns. Values originating from typed IEC-101/104 single/double points render clearly as `Single Point ON`, `Single Point OFF`, `Double Point ON`, or `Double Point OFF`. Unrecognized or analog values remain raw (never guessed). The original PreviousValue/NewValue remain intact in evidence/archives and tests; text translation is presentation-only.

The visible Events timeline is chronological oldest-to-newest, so ordinary `ScrollIntoView(last)` follows the latest event at the **bottom**, consistent with Trace. The bounded SOE store remains authoritative; manual scrolling freezes only visible Events rows without disabling acquisition. Resume is a small button shown **only when auto-follow is OFF**. When auto-follow is enabled the header control is hidden to reduce visual noise. The WPF list should append bounded batches rather than resetting all visible rows whenever possible.



## 2026-10-30 R3-B/C GI/Command observation policy
- Header **EVENT** increments only when one factual row is appended to the bounded Event Log (digital edge, spontaneous change, command stage, or GI milestone), not on every measurement or poll.
- GI is a first-class filtered event group. One GI request, ACTCON and ACTTERM each create a concise event milestone; interrogated COT20..36 data accumulate typed frame/object counts without producing repetitive SOE. A reported ACTTERM explicitly states *coverage unknown* unless a project-approved expected mapping proves it.
- Command stage messages include an exact case reference when the casebook can correlate the immutable frame identity. Missing matching case is uncorrelated evidence, never a guessed case number.
- Relay timestamps are authoritative for time-tagged process events; untimed command/GI events have explicit PC host timestamp provenance. Raw HEX and source refs stay in Trace and exports.


## R3-D source-qualified cross-workspace navigation and offline parity (2026-10-30)
Each command or GI event may carry **typed** `TransactionKind`/`TransactionCaseId`, never inferred by parsing its visible title. Case lookup additionally requires same protocol/physical source. Open Frame requires original physical evidence reference (stream+offset or active sequence) and source+protocol. An expired bounded frame is reported missing and must never be replaced by a same-IOA guess. GI case and command case tables remain bounded, virtualized and rendered only while visible.

Verified imported capture reconstructs GI and command milestone Events from the same typed decoder and reconstructs both casebooks with no TX. A truncated source yields observed incomplete; data snapshots do not become invented SOE. The relay timestamp field remains distinct from host arrival.
