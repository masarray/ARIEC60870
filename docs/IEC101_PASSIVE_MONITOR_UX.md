# UX Specification — IEC-101 Passive Monitor (compact WPF engineer cockpit)

Status: design only, subject to implementation validation  
Related: docs/IEC101_PASSIVE_MONITOR_PRD.md, docs/IEC101_PASSIVE_MONITOR_ARD.md, issue #19  
Baseline: existing ARIEC60870 WPF styling, virtualized Trace/Inspector/Evidence system.

## 1. Decision: separate workspace, shared visual system

Implement a dedicated **IEC-101 Passive Monitor** workspace in the same desktop app. Reuse existing palette, typography, high-density grid, frame inspector, value/event view models and field formatting. **Do not** reuse the IEC-101 Active Master setup page unchanged because its controls imply that the tool can send GI, reset link and controls; these have no place in a safety-first listen-only analyzer.

Keep mode selection at top level:

    IEC-101 Master | IEC-101 Dual Link | IEC-101 Passive Monitor | IEC-103 | IEC-104

Entering Passive Monitor switches to a mode-specific host panel and restores passive settings (not active-master serial settings). No duplicate data grid implementation: extract reusable display/data-grid components only after a small integration spike proves it is worth doing; otherwise use existing bindings and styling through a new partial MainWindow feature module.

## 2. Information architecture / screen priorities

Highest-value field tasks, in order:
1. Know tap is recording both physical transmit directions, and whether capture evidence is healthy.
2. See exchanges in original chronological order, with what master asked and what RTU answered.
3. See what went wrong immediately before recurring 30-second active-path changes.
4. Click to examine one frame's raw hex, link control, addressing, ASDU values and related transaction.
5. Export full evidence or the incident window without losing raw bytes.

Primary tabs inside Passive Monitor:
- **Conversations** (default): merged request/response ledger with estimated response timing, retry, status, findings.
- **Raw Trace**: one frame per row, including invalid/noise spans; raw + decoded correlated inspector.
- **Investigation**: timeline of observed state transitions, suspected switch intervals, markers, comparison per path if available.
- **Values & Events**: read-only point snapshot and event records, distinguishing event time from PC receipt time.
- **Capture Health**: two/four-port counts, integrity, gaps, OS serial errors, performance metrics, disk and adapter profile.
- **Report / Export**: investigation package first, optional readable PDF/CSV thereafter.

Use 1 compact tab strip, not 12 unrelated tabs or nested deep navigation.

## 3. Desktop layout at 1366x768 to 1600x900

Shell sections:
1. **Compact mode header** (36–44px target): “IEC-101 Passive Monitor” + neutral “RECEIVE ONLY” indicator + Start Recording / Stop & Finalize / Export.
2. **Setup strip** (48–68px): preset [Single link · 2 COM], COM [Master TX] [RTU TX], framing profile (e.g. 9600 8E1), Advanced button, folder icon. Advanced unfolds widths, flow-control/RTS/DTR hardware checklist and driver IDs. Do not permanently display full raw serial settings.
3. **Capture status strip** (28–38px): capture state, stream A/B healthy, cumulative bytes, valid/bad frames, integrity “No loss detected” vs “Partial” vs “Unknown”, disk remaining. Colors communicate exceptions, not decoration.
4. **Main tab content**: dense data table taking 60–70% available height. Inspector split underneath or to right depending width.
5. **Bottom status line** (~22px): UTC/monotonic timebase quality, loaded bundle state, count visible vs recorded, dropped/error count, stream status.

Suggested ASCII layout (structural intent, not pixel-perfect):

    ┌──────────────────────────────────────────────────────────────────────────────┐
    │ IEC-101 · Passive Monitor   [RECEIVE ONLY]      [Start] [Stop] [Export .ariec]│
    ├──────────────────────────────────────────────────────────────────────────────┤
    │ One Link · 2 COM   SCADA TX [COM7]  RTU TX [COM8]  9600 8E1  [Advanced]      │
    ├──────────────────────────────────────────────────────────────────────────────┤
    │ ● Recording    M→R ●  R→M ●   Bytes 1.2 MB   Bad 2  Loss: NOT DETECTED      │
    ├──────────────────────────────────────────────────────────────────────────────┤
    │ Conversations | Raw Trace | Investigation | Values | Health | Report         │
    ├──────────────────────────────────────────────────────────────────────────────┤
    │ Filter  [All links] [Both directions] [All functions] [Errors] [Find] [Freeze]│
    │                                                                              │
    │ Time (UTC ms)   Direction  Function          Address   Latency     Flags     │
    │ 14:20:01.023    SCADA→RTU  Request Class 2   LA 105    ...         ...       │
    │ 14:20:01.061    RTU→SCADA  User Data          LA 105    ~38 ms      ...       │
    │ 14:20:01.222    SCADA→RTU  Request Class 1   LA 105    ...         ...       │
    │ 14:20:01.246    RTU→SCADA  E5 ACK             ?         Ambiguous   ...       │
    │ ...                                                                          │
    ├──────────────────── Selected frame / exchange inspector ────────────────────┤
    │ Header  ·  Link fields   |  ASDU  ·  Type/COT/CA/IOA    |  Hex + field spans │
    │ Related: request #1023   response #1024   evidence offset + source          │
    ├──────────────────────────────────────────────────────────────────────────────┤
    │ REC: 1,274,443 bytes | View: last 1,000 rows | UTC uncertain ±driver jitter │
    └──────────────────────────────────────────────────────────────────────────────┘

This is a purpose-built workspace, not the current dual-link *active* Redundancy workspace with labels changed.

## 4. Conversations view (operator first)

- One line per observed frame by default, visually indent related reply beneath request; an option collapses a transaction into a single summary row.
- Columns default: timestamp, physical link (if 4-COM mode), direction, service, link address, response estimate, issue flag. Keep advanced columns (Type/COT/CA/IOA/FCB) configurable, not all 18 columns always on screen.
- Single-click selects, double-click goes to Raw Trace with original byte offset.
- Color only small direction glyph/text and error strip; no entire heavy saturated rows. Use blue for SCADA→RTU, muted teal for RTU→SCADA, restrained amber for ambiguous, red for verified corruption.
- If a request has no captured reply, display “No reply observed in monitored window”, not “RTU failed”.
- Provide “Show adjacent 30 s”, “Copy frame & context”, and “Pin as investigation marker” actions in context menu.
- “No traffic yet” empty state explicitly says COM open is not IEC-101 link proof and shows wiring role checklist.

## 5. Raw Trace (ASE2000 function parity, modern compact presentation)

- Monospace hex field and decoded field hierarchy visible simultaneously via resizable splitter; not a duplicate grid with a giant scrollable text block.
- Selecting a decoded control field highlights the exact raw byte(s); selecting raw byte highlights parsed field when known. Invalid/noise spans highlight their origin rather than invent interpretation.
- Adjustable decimal/hex numeric display, copy selected bytes, whole frame, or “frame+timestamp+port+profile”; row number and absolute per-port byte offset always shown.
- Raw Trace includes routine Class 1/Class 2 Link Status, no-data responses, E5, malformed or unexpected data even if Conversations filters it.
- Filtering is view-only; status reports “showing N of M captured records”, and exporting the **full session** must not silently export only filtered rows.
- Gap/uncertain clock marker visible across whole timeline.

## 6. Investigation tab (troubleshooting 30-second cycle)

Top: narrow temporal overview lanes:
- single link: SCADA requests, RTU responses, errors and observed traffic activity,
- four-COM: Link A master/RTU activity and Link B master/RTU activity, with confidence-labeled transitions.

Below: event list sorted:
“last valid response”, “request without observed reply”, “FCB retry”, “link status still acknowledged”, “application traffic ceased”, “possible switch”, “application traffic resumed”.

Right/bottom: selected event evidence chain; compare 120 s before and after. A single-link view must show a visible disclaimer: **Other redundant link not observed; switch not independently proven**. Four-COM mode can show traffic promotion/demotion inference only, not certainty about SCADA's internal active-state flag.

Default suspected period is 30 seconds as an investigation preset, **not** a hardcoded protocol timeout. Use user-editable window and multi-cycle trends.

## 7. Setup UX safety

Select profile:
- “1 link · 2 RX ports” by default.
- “2 redundant links · 4 RX ports” when implementation reaches P3.
- “Offline capture replay” is fully hardware-independent.

Source-role row shows explicit physical endpoints:
- COM monitor RX-1 = monitor SCADA TX
- COM monitor RX-2 = monitor RTU TX
- For two paths: Link A + Link B source pairs.

Use simple concise checkboxes before enabling first field capture:
- Production SCADA↔RTU wiring left intact (including any handshake).
- Monitor TXD, RTS and DTR **not connected** to plant.
- Tap interface has appropriate electrical loading/isolation and approved ground reference.
- RS-232 interface confirmed; RS-485 requires alternate setup.
- Baud/framing/field-size details reviewed.

No checkbox alone guarantees safety; this is a user preflight and manifest record. The app cannot electrically inspect a cable. Never assume DB9↔RJ45 pin numbering is universal.

On one COM open failure: show failed source clearly and close opened handles unless user explicitly chooses a degraded diagnostic continuation; never silently claim a complete two-direction capture.

## 8. Recording lifecycle / language

Use distinct verbs:
- **Start Recording**: starts both monitor receive and disk journal.
- **Freeze View / Resume View**: changes UI scrolling only.
- **Add Marker**: records investigator note without stopping acquisition.
- **Stop & Finalize**: closes input sessions, flushes and validates archive.
- **Export Investigation Package**: produces shareable .ariec V2 and report.
- **Open Capture**: replays old sessions offline.

Avoid a “Pause Capture” control in first release because it deliberately loses data between windows and looks deceptively continuous. If later required, represent a captured gap record and continuity warning.

Status language:
- “Monitor COM Ready”: local Windows handle open, not proof of end-to-end IEC-101.
- “Traffic Observed”: bytes received, not necessarily valid protocol.
- “IEC-101 Frames Seen”: valid FT1.2 messages observed.
- “No loss detected at application”: bounded claim (not proof of physical completeness).
- “Integrity Partial”: known loss, disk error or unclean capture.
- “Clock estimate”: timestamp from USB/Windows reception, not wire-level bit timestamp.

## 9. Responsive / performance behavior

- 1366x768: compact top strips + full trace + bottom inspector of ~170px, splitter resizable; avoid giant cards/font.
- 1024px or below: inspector becomes collapsible bottom drawer; critical recording buttons remain visible.
- Width >1600px: inspector can dock right, with trace keeping minimum practical width.
- virtualize rows, render batched at existing 75ms timer, reuse grid recycling; no per-frame expanding tree or layout animation.
- large archive browsing: indexed pages with async seek, not load entire capture as ObservableCollection.
- freezing view: no loss, no unbounded deferred queue; current disk counters continue refreshing.
- Keyboard: Space toggles inspector focus only when safe, Ctrl+F finds, Ctrl+C copies contextual text, Home/End navigates visible trace, Esc closes popup.
- Preserve Windows accessibility labels and readable contrast; primary state encoded with both text and color.

## 10. UX acceptance tests

UX-01: first-time engineer identifies correct COM roles and receives zero TX warning without opening command menus.
UX-02: one COM missing -> cannot falsely show dual-direction Ready.
UX-03: any active Master “Send GI/Reset/Command” action is absent/unreachable in passive mode, including keyboard shortcuts.
UX-04: after 10k frames, data grid remains responsive while full raw archive is unchanged.
UX-05: View Freeze and filter changes do not change recorded byte count/file hash.
UX-06: investigator selects “possible switch” and reaches exact before/after raw evidence in ≤3 UI interactions.
UX-07: offline import works without adapters connected and shows V1 limited-evidence warning.
UX-08: viewing a corrupted V2 bundle explains failed integrity clearly; does not mislabel the capture successful.
UX-09: one-link mode never displays the other link as healthy/failed without observed data.
UX-10: 4 COM screenshot can distinguish all four physical TX directions and session integrity without scrolling.

## 11. Implementation recommendation

Build a dedicated passive workspace host that reuses existing ARIEC visual components selectively. Reusing the existing IEC-101 **Master UI wholesale** is unsafe and cluttered; rewriting all shared grids and inspector from scratch is wasteful. A purpose-built screen with reused reusable primitives is the best balance of reliability, small code changes, user friendliness and long-term maintainability.

Do not implement a waveform/Carrier Detect plot from USB COM receipt timestamps. If a physical Line Analyzer is later commissioned with suitable hardware, make it a separate hardware-backed visualization explicitly labeled by capability.
