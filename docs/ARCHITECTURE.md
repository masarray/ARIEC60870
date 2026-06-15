# ARIEC60870 Protocol Lab Architecture

## Product Role

ARIEC60870 is now positioned as an Apache-2.0 clean-room **ARIEC60870 Protocol Lab** for IEC 60870-5-103, IEC 60870-5-101, and IEC 60870-5-104 evidence sessions.

The primary product role is:

> **Single-connection IEC 60870 analyzer/tester that presents each protocol with its own setup, grid columns, decoder language, and evidence model.**

IEC-103 remains the mature protection-relay path. IEC-101 and IEC-104 are expanded as practical telecontrol analyzer paths with protocol-aware UX and clean-room 10x ASDU/APCI decoding. Dual-link/NUC-style redundancy is intentionally out of scope for the baseline analyzer.


## Protocol-Aware UX Contract

The desktop UI must never expose irrelevant fields for the selected protocol.

```text
IEC-103
  Setup : serial COM, baudrate, link address, Class 1/Class 2, reset link/FCB, FUN/INF mapping
  Grid  : ASDU, COT, Class, ACD/DFC, FUN, INF, mapped signal, relay timestamp

IEC-101
  Setup : serial COM, baudrate, link address, common address, Class 1/Class 2, GI, clock sync
  Grid  : Type ID, VSQ, COT, CA, IOA, value, quality, timestamp

IEC-104
  Setup : TCP host/port, common address, STARTDT runtime, GI, clock sync
  Grid  : APCI I/S/U, N(S), N(R), Type ID, COT, CA, IOA, value, quality
```

This keeps the analyzer from feeling like an IEC-103 application with 101/104 bolted on.

## Reference Learning From IEC101MasterTester

The IEC101MasterTester reference implementation shows a useful product pattern:

- one master service owns the active protocol communication
- UI/analyzer layers observe callbacks/evidence
- line monitor is technical evidence
- event log/status history are operator-facing
- findings are rule-based diagnostic output
- GI is a one-shot action, not cyclic polling
- ACD is an important link-layer fact
- Class 1 should be prioritized only when ACD=1
- Class 2 is the normal/background polling path

For ARIEC60870 we keep this pattern, but we remove:

- dual redundancy
- NUC workspace
- slave simulator scope
- external protocol-stack dependency

## Layers

```text
ARIEC60870.Desktop / ARIEC60870.Cli
  Operator commands, protocol-aware setup, evidence display, report export

ARIEC60870.Master
  IEC-103 serial master session
  IEC-101 serial master session
  IEC-104 TCP client session
  FT1.2 frame builder/parser
  IEC-10x ASDU builder/parser
  IEC-104 APCI/APDU parser
  controlled polling / STARTDT / GI policy
  protocol-aware evidence and findings

ARIEC60870.Core
  FT1.2 parser
  IEC-103 ASDU decoder
  offline analyzer
  semantic findings
```

## Active Master Runtime v0.4

```text
Open Transport
  -> optional startup delay
  -> optional Reset Remote Link
  -> Reset FCB
  -> optional Clock Sync
  -> optional General Interrogation
  -> bounded Class 1 GI follow-up
  -> normal Class 2 cycle
  -> if ACD=1, bounded Class 1 event drain
  -> if DFC=1, busy backoff
  -> if timeout burst, controlled recovery
  -> evidence log every TX/RX/state/finding
```

## Master Polling Policy

Bad policy:

```text
Request Class 1
NO DATA
Request Class 1
NO DATA
Request Class 1
NO DATA
```

Preferred policy implemented in ARIEC60870:

```text
Normal:
  Request Class 2 at configured interval

If secondary response has ACD=1:
  Request Class 1 until queue is drained

If secondary response is NO DATA:
  stop Class 1 drain and return to Class 2 cycle

If GI command was sent:
  allow bounded Class 1 follow-up only until GI END, NO DATA, ACD clear, DFC busy, or drain limit

If DFC=1:
  backoff

If timeout:
  record evidence
  use bounded recovery
```

## Evidence Philosophy

ARIEC60870 must not merely say "connected". It must show:

- exact TX/RX frame
- timestamp
- sequence number
- master state
- polling reason
- direction
- link address
- response time
- control function
- PRM/FCB/FCV/ACD/DFC
- ASDU type/COT/FUN/INF/DPI when present
- GI lifecycle
- Class 1 drain behavior
- timeout/busy/checksum evidence
- engineering recommendation when behavior is suspicious

## WPF Boundary v0.5

`ARIEC60870.Desktop` is the first desktop shell for the master engine. It intentionally stays thin:

- edits single-connection master settings
- starts/stops `Iec103MasterSession`
- listens to `Iec103MasterEvidenceEvent`
- listens to `Iec103MasterFinding`
- displays live KPI counters, evidence rows, selected-frame detail, and findings
- exports Markdown evidence report using the same report writer as CLI

WPF must not own protocol behavior. Polling policy, state transitions, frame building, parsing, and findings remain in `ARIEC60870.Master` and `ARIEC60870.Core` so the engine is still testable without UI.

## Clean-Room Boundary

This repo must remain independent:

- no external protocol-stack source
- no unrelated open-source protocol-stack source
- no packet-dissector source
- no commercial protocol-stack source
- no copied state machine from third-party libraries

System.IO.Ports may be used for serial I/O only. IEC-103 framing, polling, decoding, and evidence logic are written in this repository.

## v0.6 Demo Transport Layer

ARIEC60870 v0.6 adds `SimulatedRelayTransport` as an internal deterministic IEC-103 slave test harness. It implements `IByteTransport`, so the same master state machine can run against either a real serial relay or a simulated generic relay-like endpoint.

This is intentionally not a full slave stack. Its job is to validate the master product workflow and regression-test the polling policy:

```text
Reset FCB -> ACK
GI command -> ACK + seed Class 1 event queue
Class 1 request -> DPI(TM)
Class 1 request -> DPI(RT)
Class 1 request -> Identification
Class 1 request -> GI END
Class 1 request -> NO DATA
Class 2 background -> NO DATA, optionally with ACD=1 when events are pending
```

The important engine rule added in v0.6: a secondary NO DATA response can still carry ACD=1. The master preserves that ACD information so a Class 2/background poll can trigger bounded Class 1 event-drain without falling into continuous Class 1 bombardment.

## v0.8 user mapping profile and event/value separation

ARIEC60870 now separates three concerns clearly:

```text
Universal IEC-103 decoder
  FT1.2, control field, ASDU Type/COT/FUN/INF/DPI/timestamp

User mapping profile
  Project-owned FUN/INF/type -> signal name/group/state map

Runtime views
  Value Viewer = current snapshot
  Event Log = relay timestamped state-change/edge events
```

There is no built-in vendor signal profile. The built-in decoder only interprets protocol-level fields. Final signal names must be supplied by the user/project mapping profile.

Event Log rule:

```text
Use relay timestamp from the ASDU time field.
Do not use PC arrival time as event time.
GI/status snapshots update Value Viewer.
State changes and spontaneous relay events update Event Log.
Keep raw FUN/INF/hex beside mapped names.
```

This avoids the dangerous failure mode where a tester presents guessed relay signal names or floods the event log with GI snapshots.


## Assessment Layer

The v1.0 assessment layer converts master session evidence into a FAT/SAT-oriented checklist. It checks communication activity, FT1.2 frame quality, GI completion, polling policy, timeout behavior, value acquisition, relay timestamp quality, mapping coverage, and findings. It is a review aid, not a replacement for raw evidence.

## v1.1 Output and Performance Layer

The desktop UI is no longer raw-hex-first. The output hierarchy is:

```text
Operator Evidence  -> readable engineering meaning/action
Value Viewer       -> current relay snapshot
Relay Event Log    -> relay-timestamp edge/state-change events
Frame Trace        -> raw hex/protocol transparency
```

Raw frame evidence remains mandatory, but it is exposed in the advanced Frame Trace tab, selected evidence inspector, and report appendix. Normal users should be able to understand the test outcome without reading hexadecimal bytes.

For high-volume polling, ARIEC60870 uses two levels of protection:

1. **Engine retention limits** keep retained evidence/event/finding lists bounded while full counters continue to represent session totals.
2. **WPF render buffering** queues evidence callbacks and flushes them in timed batches, using virtualized DataGrids and bounded visible rolling windows.

This prevents the UI from becoming an unbounded append-only frame renderer during long FAT/SAT sessions.


## v1.2 Stop-Safe Desktop Control and AR Visual Identity

The desktop app now treats stop/cancel as a first-class runtime workflow. A user stop request cancels the active session and asks the active transport to close, which releases blocking serial read/write operations on typical USB/RS485 adapters. The UI restores Start/Stop state in the session finalizer and keeps a force-close path available while shutdown is in progress.

The WPF skin is aligned with the mature engineering software identity: soft white cards, restrained blue accent, segmented tabs, modern data grids, slim scrollbars, and operator-first evidence panels. This visual layer must not own protocol state; it remains a cockpit over `ARIEC60870.Master`.

## Diagnostics pipeline note

Runtime faults and recoverable transport exceptions are routed to Diagnostics rows. The UI must remain operator-safe: no unhandled serial timeout should block the Start/Stop workflow, and every exception worth escalating must be selectable/copyable from the Diagnostics tab.

