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


## 2026-10-30 — Event → original Frame navigation, calm field workspace

For every retained Event row, retain the **same immutable typed source evidence object** used when the Event was recorded. This does not duplicate the raw frame or build an unbounded all-frame UI table: Event history is already capped. Link A/B, protocol, stream ID, byte offset and active physical-frame sequence must all match the clicked event reference. Never search by just IOA, timestamp or HEX coincidence.

**Open Frame** checks the 1200-row live Trace ring first. If the exact frame was already evicted, it projects the Event row's retained source evidence into one **historical original-frame view** on the Frame Trace tab, selects it and shows full Frame Properties/HEX. Trace is placed on HOLD; continuous capture continues without freezing or writing to the plant. Clicking RESUME replaces the temporary view with the current live ring. The historical single-frame view is not represented as a complete chronological transcript. Captures with no available raw provenance still report honestly unavailable rather than substitute an unrelated frame.

For case stage navigation, exact source-qualified Event evidence can provide the same fallback. This is a within-session fix and **not** full indexed disk-journal seeking for evicted Event/case histories. To keep multi-hour, all-frame evidence beyond the bounded Event history, explicitly enable full raw recording in Setup before starting IEC-101 Passive Monitor. Selected .ariec ZIP imports remain bounded previews.

**Primary field navigation:** Values, Event Log, Frame Trace, Controls, GI, Report, and optional Redundancy. The redundant Messages/Evidence Ledger, Smart Findings and Capture *navigation buttons* are hidden in Passive mode; their protocol/diagnostic/evidence and raw recording capabilities are not deleted. Full recording remains in Setup and Open Capture/Save Frames/Trace TXT remain in the left rail. Underlying internal tab IDs are intentionally stable to avoid breaking source navigation and existing commands. Neither first-use UI nor protocol-mode switch should select the hidden Capture workspace.

**GUI acceptance:** on the actual WPF window, produce a real decoded IEC-101 Event, evict its source from the active Trace ring, click Open Frame and assert exact physical identity, selected frame, Trace focus and Hold state, followed by Resume. No COM hardware and no second wire decoder needed for the regression. Physical COM/FAT still required.


## R3-E command semantic qualification (2026-10-30)

A secondary-direction control ASDU with COT=6 is an unexpected direction/COT combination rather than an RTU ACTCON; valid FT1.2 checksum is not sufficient. Such a frame remains visible and may be reported as a separate protocol anomaly but does not consume a pending SELECT. A command ASDU with COT TEST flag never produces an unqualified operator-facing `Command accepted` title. When the command casebook discards older stages beyond its 24-stage bound, copied evidence explicitly states that the history is truncated. The true RTU internal cause cannot be inferred from IEC-101 negative ACTCON alone. See `docs/R3E_VERIFIED_HISTORICAL_COMMAND_FORENSICS_20261030.md`.
