# ARIEC60870 Master Runtime — Scoped Engineering Contract

These instructions apply to `src/ARIEC60870.Master/**` and extend the repository root `AGENTS.md`. The root file remains authoritative for product identity, IEC-103 polling policy, UI/output policy, clean-room rules, release behavior, and simulator boundaries.

## 1. Runtime authority

`ARIEC60870.Master` owns active-master session state, transport orchestration, polling policy, FCB/FCV behavior, Class 1 drain decisions, startup sequence, timeout/backoff, and live protocol evidence.

Desktop/CLI code may command and observe the master; it must not become a second master state machine.

## 2. Root-cause-first rule

For session, polling, serial, GI, ACD, DFC, timeout, Stop, or reconnect defects:

`reproduce → capture evidence → trace state transition → identify owner → fix → regression test → validate cleanup`

Do not add timers, delays, extra polling, duplicate flags, or UI-side recovery until the actual state transition and transport behavior are understood.

If three patches in the same runtime subsystem still chase symptoms, STOP before patch four and re-audit the state machine, transport ownership, cancellation path, and protocol evidence.

## 3. Explicit state machines

Connection/session progress must be represented explicitly rather than inferred from scattered booleans.

Expected states should cover the actual lifecycle, for example:

`Idle → Opening → Starting → Running → Stopping → Completed/Faulted → Idle`

Protocol subflows such as GI and Class 1 drain should also have explicit bounded progression.

Unexpected input must produce a defined state/result, not an undefined transition or infinite retry.

## 4. Result-oriented failure architecture

Expected runtime outcomes should use typed Result/status values rather than exception-driven normal control flow.

Examples:

- timeout/no data;
- DFC busy;
- invalid or rejected secondary response;
- transport closed during Stop;
- cancellation;
- exhausted bounded retry/drain;
- unsupported operation;
- malformed frame reported by Core;
- serial device unavailable.

Infrastructure exceptions from serial/.NET I/O should be caught at the transport/session boundary and converted into a structured runtime result plus diagnostic evidence where recovery is possible.

Do not silently swallow exceptions. Do not use throw/retry loops as a protocol state machine.

## 5. Stop/cancellation is a first-class path

Stop is not an exceptional afterthought.

- one owner controls the active session cancellation token;
- transport close may be used to release blocked serial operations;
- close/dispose races during cancellation are expected boundary conditions and must be contained;
- finalization must run exactly once;
- session state must converge to a stable terminal/idle state;
- no stale callback may revive a stopped session.

Never rely on arbitrary sleeps to make Stop reliable.

## 6. Polling and backpressure

Preserve the root polling contract:

- Class 2 is normal/background polling;
- Class 1 drain is triggered by protocol evidence such as ACD or bounded GI follow-up;
- NO DATA/ACD clear ends unnecessary Class 1 drain;
- DFC causes bounded backoff;
- timeout retry/reset policy remains controlled.

Do not create an unbounded request queue. If command production can outrun the transport, use explicit bounded scheduling/backpressure.

## 7. Diagnostic pipeline

Detailed operator diagnostics are important, but the polling/receive path must remain lightweight.

Hot runtime paths should emit compact structured evidence/events into bounded storage or queues. Expensive formatting, report text, and UI-ready message composition should happen outside the critical receive/polling path where practical.

- bound queues/retained evidence;
- aggregate or rate-limit repeated normal timeout/no-data conditions;
- preserve counters for full-session totals;
- never allow diagnostic persistence/UI consumers to block master progress;
- diagnostic failure must not corrupt protocol state.

## 8. Transactional configuration/session promotion

New transport/session/polling configuration should be validated before it replaces active/last-known-good state.

`parse → validate → prepare → activate`

If opening or preparation fails, do not leave a half-active session whose UI state claims Running.

## 9. Resource ownership

Every session has deterministic ownership for:

- serial/network transport;
- timers;
- cancellation sources;
- receive loops;
- polling tasks;
- event subscriptions;
- retained evidence buffers.

Repeated Start/Stop and reconnect cycles must not accumulate tasks, timers, handlers, or open handles.

## 10. Performance and regression evidence

For changes to polling, receive loops, evidence retention, or cancellation, measure/inspect relevant before/after behavior when practical:

- poll cadence/jitter;
- command/response latency;
- CPU during sustained monitoring;
- memory/evidence growth;
- Stop latency;
- retained queue depth;
- repeated Start/Stop resource count.

A repeatable regression of roughly >10% in a relevant metric requires investigation and explicit justification.

Every bug fix should add the highest-value deterministic Master test practical, especially for timeout, DFC, ACD/Class 1 drain, cancellation, transport close, and finalization.

## Final rule

The master runtime must remain deterministic even when the relay is silent, slow, busy, malformed, disconnected, or the operator presses Stop at the worst possible moment.
