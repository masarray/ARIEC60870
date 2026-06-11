# ARIEC60870 Output and Performance Policy

ARIEC60870 is an active IEC-103 master tester. The application must not force every user to read raw hexadecimal frames. Raw hex is mandatory for protocol transparency, but it is an evidence layer, not the primary operator language.

## Output hierarchy

### 1. Operator Evidence

The primary live view must explain what happened in readable engineering language:

- Master is performing normal Class 2 background polling.
- Relay responded: no requested data available.
- Relay value received: mapped or unmapped FUN/INF point.
- General Interrogation completed.
- Relay is busy; ARIEC60870 backs off.

This view is for users who understand relay testing but do not want to decode byte streams manually.

### 2. Value Viewer

The Value Viewer is a current snapshot. It should show one row per current relay point:

- Signal name if a user mapping profile exists.
- Raw FUN/INF fallback if no mapping exists.
- Current state/value.
- Relay timestamp when available.
- COT, ASDU type, mapping status, and raw evidence.

The Value Viewer must not become a frame log.

### 3. Relay Event Log

The Event Log is edge/state-change oriented.

Rules:

- Event time is the relay timestamp from IEC-103 ASDU time fields.
- PC arrival time may be retained as metadata but must not become the event time.
- GI/status snapshot updates Value Viewer only.
- State change or spontaneous event becomes Event Log.
- Raw FUN/INF and raw hex must remain visible as evidence.

### 4. Frame Trace

Frame Trace is the advanced protocol transparency view:

- TX/RX direction.
- Raw hex.
- FT1.2 frame type.
- Control field interpretation.
- ACD/DFC/FCB/FCV.
- ASDU Type/COT/CA/FUN/INF.
- Relay timestamp.
- Protocol meaning.

This tab is for protocol experts, R&D review, and deep troubleshooting.

## Performance policy for high-volume polling

IEC-103 polling can produce large frame volumes during long FAT/SAT sessions. ARIEC60870 must stay responsive by design.

### Memory guardrails

The master engine keeps full counters but bounded retained records:

- `MaxRetainedEvidenceEvents` for retained frame/evidence records.
- `MaxRetainedRelayEvents` for edge/state-change events.
- `MaxRetainedFindings` for findings.

Old retained records may be dropped from memory, but counters continue to reflect the whole session.

### Snapshot model

The UI must prefer snapshots over append-only rendering:

- Value Viewer is a dictionary-like current snapshot.
- Relay Event Log is edge-only, not every frame.
- Operator Evidence is a bounded rolling window.
- Frame Trace is a bounded rolling window.

### Render throttling

The WPF UI must not update one row synchronously per incoming frame. Evidence events are queued and flushed in timed batches through a background-priority dispatcher timer. This avoids freezing when a relay or simulator produces a high frame rate.

### UI virtualisation

All large DataGrid views must enable row/column virtualization and recycling mode.

### Report behavior

Reports must state when retained evidence is bounded. Reports should include counters for dropped retained events so reviewers understand that high-level counters represent the complete session while raw retained trace may be windowed.


## Stop-Safe Rendering and Serial Shutdown

High-volume polling can coincide with a user pressing Stop. The UI must not become trapped in an indeterminate disabled state. Stop should cancel the active token, close the active transport, allow the session task to unwind, and then restore Start enabled / Stop disabled. The app may keep Stop visible as a force-close action while the session is shutting down.

Serial read code should not depend on unbounded asynchronous reads that ignore cancellation. Use configured serial timeouts and tolerate transport close while a read is pending.

## Diagnostics pipeline note

Runtime faults and recoverable transport exceptions are routed to Diagnostics rows. The UI must remain operator-safe: no unhandled serial timeout should block the Start/Stop workflow, and every exception worth escalating must be selectable/copyable from the Diagnostics tab.

