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

## OBS-P0 Stage A — stable live WPF projections (2026-10-31)
The bounded dispatcher-owned UI collections emit supported single-row Add/Remove/Replace notifications for routine traffic instead of WPF Reset on each 75ms flush. Tail reconciliation preserves matching frame references and selected view anchors; keyed reconciliation preserves unchanged Values and GI/Command Case rows, while a truly different session/archive scope may Reset. Switching tabs never abandons an operator-selected history/archive/held inspection. Quick Find invalidates only the projection whose query changed; count updates coalesce via the Dispatcher after collection changes. Existing protocol decoder, raw journal, master/control safeguards, case verdicts and passive zero-TX boundary are unchanged. Validate UI flicker/scroll and DPI manually; automated CI is not field acceptance. Basis: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls and https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-data-binding.

## OBS-P0 Stage B — observer presentation budget
Use one active search layout, and keep the complete archive search off the live filter's hot path. Collapsed command dock consumes **0px** and cannot appear in receive-only capture; hidden inspector consumes **0px**, can be reopened without regenerating selected frame data. Trace retains its original bounded ListBox and virtualization; summary and raw HEX remain inspectable without synthesizing values. No wire/protocol engine changes. Manual 1366x768 and 1540x960 multi-DPI visual measurements and 1200bps hardware FAT remain unverified after software CI.

## OBS-P0 C acceptance metrics and evidence
The Windows CI pipeline now archives actual WPF viewport PNGs plus `metrics.json` as a separate 14-day `ARIEC60870-OBS-C-ui-evidence` artifact. Bounded 1,400 synthetic fixed FT1.2 frames are admitted in batches of 25 on the dispatcher, and the test records incremental `NotifyCollectionChangedAction.Reset` count after initial mount, frame count, elapsed frame-feed/flush interval and Trace current-window filter-count consistency. This is reproducible GUI integration evidence, NOT a physical UART bandwidth benchmark and NOT a 2–4 COM field acceptance. Avoid comparing runner time to an arbitrary desktop absolute ms target. Target: zero collection-wide steady-state Reset after initial mount, <=1,200 bound visible frames, no held selection jump. Tests run only via explicit CLI argument; no new production background timer or telemetry upload.

## OBS-V2 — Meaning-first Frame Trace and decoded object drill-down (2026-10-31)

**Operator intent:** The main product is a master protocol tester with a live and offline evidence-oriented GUI, not a separate preflight/capture CLI. Frame Trace defaults to **Activity focus**, reducing repeated poll/status/no-data noise **only in the visible WPF filtered view**; **All frames** remains one click away and is always used by exact evidence navigation. The underlying bounded source ring, persistent raw journals (if enabled), frame byte strings, TX/RX totals, field timestamps and parser results are never modified by focus. Unknown, malformed, E5 (contextual), DFC/ACD, decoded ASDU, negative and test messages remain visible; this is not a forensic capture filter.

**One original frame per Trace row:** The compact second line uses the existing canonical decoded ASDU: Type ID, source COT and P/N/TEST flags, Common Address, number of parsed/declared objects and up to two IOA/value examples. Raw HEX stays one-click available via row selection and existing context actions. Any object beyond the first two is **not discarded**. Selected-frame inspector shows ALL typed objects in a bounded-height, virtualized IOA/value/quality table, with source byte evidence and relay timestamp (or explicit 'No device timestamp') available on each row. SQ=1 derived IOAs are labeled as implicit and must never be presented as transmitted address bytes.

**Performance:** Selection-only projection and lazy row digest; no extra receive-thread work, no WPF collection updates per frame, no new timers. Existing bounded Trace store and virtualized DataGrids remain authoritative for rendering. The field-facing acceptance must inject valid IEC-101 spontaneous and multi-object data via the real passive decode pipeline, capture populated Values/Events, and validate the selected two-object WPF inspector in a screenshot. Windows CI viewport size clamping remains explicitly documented and is not physical DPI proof.

**Acceptance boundary:** This is an implementable readability/observation milestone, not claimed full parity with any specific commercial tool, not proof of IEC101/103/104 physical interoperability, and not field-approved commissioning.

## POINT-WIRE-01 — point-to-source investigation (2026-10-31)

In the *existing* live Value Viewer an engineer can select an IOA and immediately open the exact original telegram through the already implemented source-qualified in-memory or session-history navigator. Multi-object 101/104 ASDUs select the clicked IOA in the existing virtualized frame inspector rather than the first sibling. A missing evidence reference disables navigation; there is no IOA/time-only substitution. Double-click, Open Frame, and copy point/quality/source/raw HEX are direct operator actions. Live updates replace selected rows by physical key without losing selection or forcing the operator viewport to jump.

Values filters are **All points / Quality flags / Unmapped / Recently changed / No device time**, computed only from the bounded visible ValueRows projection; no change to authoritative point identities, raw journal, master polling or decoder. Quality flags use typed source-backed IEC quality flags and never infer OV from an analog numeric boundary. No device time means none was transmitted or supported, not PC arrival time. A single-line footer provides selected process value, CA/IOA, sender quality, time availability and source evidence status. No new receive-time frame match, timer or unbounded WPF grid.

Windows WPF acceptance must inject actual valid multi-object IEC101 wire frames through the production passive decoder, select point IOA 8, use the same operator Open Frame action, assert exact frame reference and IOA 8 selection and retain screenshots. It remains a software observation milestone, not a claimed physical 1200-baud/COM FAT result.

## UX-GRID-01 — grid-first field analyzer layout and discrete-state semantics (2026-10-31)

The first viewport prioritizes decoded frames and process/event rows. **Values** has a single compact search toolbar; **Event Log** has only search, count and AUTO follow; **Trace** retains the search and the follow control, with optional older/newer history paging only on demand. Open Frame, Open Case, filtering, archive search and inspector options belong to context menus instead of always occupying grid-width/height. All hidden filter state is still represented in the small row-count status so an operator cannot mistake a filtered snapshot for the entire retained evidence.

Both Value and Event columns use one consistent lightweight state chip pattern: source-typed IEC101/104 SP and DP information appears as a narrow **[SP] / [DP]** type chip followed by a **[ON]** soft-green state chip or **[OFF]** soft-rose state chip. Intermediate is amber and indeterminate neutral. Only IEC10x decoded TI 1/2/30 and 3/4/31 and exactly recognized states are enhanced; numbers (including double-point numeric 1 = OFF, 2 = ON), unknown values, commands, GI transactions, IEC103 private types and analog data must not be incorrectly labeled. Original value, Type, COT, quality, CP timestamp and raw HEX remain unchanged and accessible through the same data row/menu/inspector. Bad-quality digital points can still display the transmitted state chip, with quality flags separately marked; green ON is never a quality or command-success claim.

The change uses existing bounded ObservableRangeCollections, recycling virtualized grids, and immutable per-row state objects. No dynamic visual animation, unbounded row view models, extra timer, receive-path formatting, driver interaction or protocol engine modifications. Save WPF screenshots and test XAML layout at actual realized CI viewport dimensions, then perform physical 100%/125%/150% DPI review and COM/RTU FAT separately.

