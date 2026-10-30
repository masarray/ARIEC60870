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

For IEC-101/IEC-104 10x ASDUs, one RX frame may contain multiple information objects through VSQ/SQ sequencing. The master engine must publish/update every decoded information object as Value Viewer evidence, not only the first object in the ASDU. This is required for GI completeness in single-link and dual-link sessions.

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

## IEC-101 Passive Forensic Timeline (P2E)

The IEC-101 receive-only monitor archives both physical transmitter directions
into original, independently verified binary journals. Frame Trace is bounded
operator presentation and **must not** be used to derive the definitive timeline.

- `PassiveIec101FailureTimeline` runs in the same serial journal writer,
  observing all frames and independently derived transaction / GI lifecycle
  events. It emits a compact `timeline-windows.jsonl` index for each observed
  5-second window; default window size can be changed in code for diagnostics.
- Windows describe **received traffic**, not a proven physical outage or
  switching event. A standby FC9/FC11 Link Status cycle is normal. Keep
  host cross-adapter timestamp uncertainty visible.
- When no frames are received, represent the no-observation interval honestly.
  Expand at most 12 individual empty windows for short pauses; compress longer
  gaps to a single summary with exact omitted-window count.
- Writer scratch lists are reused per read, and GUI presentation uses a
  separate bounded dropping 64-window ring. **Only the GUI ring can drop**;
  original raw journals and the complete timeline JSONL remain on disk.
- The existing Capture status shows the recent traffic summary. Only anomalous
  windows enter existing Diagnostics; do not flood Event Log/Findings with
  routine polling or GUI fake indications.
- Offline replay recomputes the same pure timeline from complete SHA/CRC
  verified binary journals under the chosen decoder profile, independent of
  derived indexes and WPF row retention.
- Historical search/paging over many hours and four-COM redundant A/B
  supervision are subsequent milestones, not assumed complete here.

## P3: Four-port IEC-101 Redundant Passive Capture

The IEC-101 monitor supports either **two RX-only COM inputs for one full-duplex
circuit**, or **four RX-only COM inputs for two independent circuits A/B**.
The additional pair is strictly opt-in within the existing Setup.

- Original `APM1` stream journal layout is preserved for compatibility.
  Four sources write four independent authoritative raw `stream-XX.bin`
  files; every file's CRC32/sequence/offset/SHA256 is independently verified
  before offline replay may display any evidence. The saved manifest
  identifies each COM port, physical transmitter, source and circuit.
- One receive worker per COM, one bounded channel and one disk-journal writer.
  Frames, transactions, GI state and operator summaries must never be derived
  solely from the dropping GUI queues.
- Link A streams 0/1 and Link B streams 2/3 **must not cross-correlate**
  E5 responses, acknowledgements, COT7 ACTCON, or COT10 ACTTERM.
  Per-circuit P2E timeline buckets are independent.
- WPF presentation remains bounded. Source labels and value identities
  include circuit A/B, so identical IOAs on redundant lines do not overwrite
  each other silently.
- The four-way offline merge is sorted by USB/Windows host read-completion
  timestamps; it is **not** guaranteed wire-edge ordering between adapters.
  No unsupported assertion of physical 33-bit idle timing or actual NUC
  failover state is allowed.
- Two-input captures continue working without migration. All stored bytes
  and finalized hashes are immutable under decoder profile overrides.
- This P3 implementation does not replace physical testing of tap loading,
  UART handshaking, multi-hour lossless soak or electrical isolation.


## IEC-101 P4A historical evidence navigation (2026-10-28)

- Never load multi-day timeline-windows.jsonl into a full GUI collection. Build a read-only sparse index (at most one block per 128 rows), use explicit 60-row UI pages, and keep actual file offsets as stable reverse-pagination cursors.
- Run archive verification, index build, page scan and historical raw resolution away from the WPF Dispatcher. User selection must remain cancelable and must not mutate current Values/Event Log snapshots with historical observations.
- Treat derived timeline entries only as navigation hints. Present raw HEX as **verified** only after matching the exact physical RX stream and original byte offset in CRC/SHA-verified APM1 journal replay. Reject incomplete captures and stale indexes. Never conflate local source 0/1 on Link B with physical RX0/RX1; map to RX2/RX3.
- Historical selected-frame replays remain bounded in RAM but may be sequential in CPU/disk cost for large archives. Persisted random-access raw checkpoints require future FAT and benchmark proof. Do not claim physical timestamp precision, NUC switching, or loss-free electrical monitoring from USB receipt order.
- Reference: docs/IEC101_PASSIVE_P4A_INDEXED_NAVIGATION.md.


## R3-E — bounded verified portable archive search (2026-10-30)

Frame Trace now offers background, read-only full-scan search on portable `.ariec` ZIP evidence. The UI shows a maximum 150 results per page, while its normal recent live ring remains at 1,200 rows. All rows are scanned and SHA-256 / declared count are checked before a search page becomes visible; later pages deliberately repeat the verified single-pass scan rather than retaining unbounded frame objects or trusting an unverified index. This is *not* indexed random seeking into full passive binary journals. Full RX-only passive recording remains explicitly opt-in, and GUI backpressure must never be described as complete recording. See `docs/R3E_VERIFIED_HISTORICAL_COMMAND_FORENSICS_20261030.md`.
