# AGENTS.md — ARIEC60870 Engineering Guardrails

This file is the operating contract for every future coding agent, assistant, or maintainer that modifies ARIEC60870.

## Product Identity

ARIEC60870 is an **Apache-2.0 clean-room IEC 60870-5-103 Active Master Tester + Analyzer**.

It connects to protection relays / IEDs acting as IEC-103 slaves, runs controlled master polling, decodes the response, updates Value Viewer and Relay Event Log, and exports deep engineering evidence.

ARIEC60870 is **not**:

- a passive decoder only
- a vendor-specific relay tester
- a generic fake profile generator
- a dual-redundancy IEC-101 clone
- a wrapper around external, commercial, GPL, or unclear-license protocol stack source

## Product Non-Negotiables

1. **Active master first**
   - The main product is a master tester that actively connects to IEC-103 slave relays.
   - Offline decoder is a supporting mode for troubleshooting traces.

2. **Single connection baseline**
   - Dual-link redundancy and active/standby architecture may be added only when explicitly requested; keep the wording protocol-neutral and avoid customer- or country-specific convention labels in public files.

3. **Controlled polling**
   - Class 2 is normal/background polling.
   - Class 1 is event-drain only, triggered by ACD=1 or bounded GI follow-up.
   - Never bombard Class 1 while the slave keeps returning NO DATA / ACD=0.

4. **Relay timestamp for Event Log**
   - Event Log time must come from the relay ASDU timestamp when present.
   - PC arrival time is forensic metadata only.
   - GI snapshots update Value Viewer; state change / edge events update Relay Event Log.

5. **User mapping, not built-in vendor profile**
   - Do not ship guessed vendor signal profiles.
   - Built-in code may decode protocol fields only: Type, COT, FUN, INF, DPI, timestamp, quality, raw frame.
   - Signal names must come from user-owned mapping profile files.

6. **Raw evidence always visible**
   - Even when mapped signal names are shown, always retain FUN/INF/Type/COT/DPI/raw hex in UI and reports.

7. **Clean-room implementation**
   - No copied code from external protocol stacks, packet dissectors, vendor SDKs, or commercial protocol stacks.
   - Public docs and standards can guide behavior, but code must be independently written.
   - Do not paste vendor manual tables into the repo unless the user explicitly confirms legal permission.

8. **Apache-2.0 repository hygiene**
   - Keep source code, docs, landing page, sanitized samples, workflows, and legal files.
   - Exclude bin/obj/out/dist/node_modules/raw logs/private captures/secrets.

## Required Architecture Boundaries

Keep protocol logic out of WPF.

```text
ARIEC60870.Desktop
  UI cockpit only: setup, commands, live tables, export actions

ARIEC60870.Cli
  Developer/test harness and offline/batch entrypoint

ARIEC60870.Master
  Active master state machine, polling policy, transport orchestration, live evidence

ARIEC60870.Core
  FT1.2 parser, link control decoder, ASDU decoder, offline analyzer, mapping primitives

ARIEC60870.Reports / future
  Markdown/HTML/PDF reporting and evidence bundle generation
```

WPF must never become the owner of protocol state, FCB/FCV handling, Class 1 drain decisions, or ASDU parsing.

## Master Polling Policy

Correct runtime loop:

```text
Startup:
  Open transport
  Optional startup delay
  Optional Reset Remote Link
  Reset FCB
  Optional Clock Sync
  Optional General Interrogation
  Bounded GI Class 1 follow-up

Normal:
  Poll Class 2 at configured interval

If any secondary response indicates ACD=1:
  Enter Class 1 event drain
  Request Class 1 until NO DATA / GI END / ACD clear / DFC busy / max drain / timeout

If DFC=1:
  Back off and record busy evidence

If timeout:
  Retry in controlled way
  Reset FCB only after configured timeout burst
```

Forbidden pattern:

```text
Request Class 1
NO DATA
Request Class 1
NO DATA
Request Class 1
NO DATA
```

## Value Viewer vs Relay Event Log

### Value Viewer

Current snapshot table.

- updated by GI responses, cyclic/background values, and event responses
- uses latest known state/value per signal key
- can use user mapping profile for signal name/group/state map
- unmapped points remain visible as raw FUN/INF

### Relay Event Log

SOE-like relay event list.

- event time = relay timestamp from ASDU time field when available
- logs state change / edge event / spontaneous event
- does not log every repeated GI/status snapshot
- stores PC arrival time only as forensic metadata

## Mapping Profile Rules

Mapping profile is a user/project asset.

Allowed:

- JSON schema/config for user-defined mapping
- import/export profile
- profile validation
- sample profile clearly labeled as example user mapping

Forbidden:

- `vendor-specific basic profile` unless the user supplies validated data and asks for that profile
- guessed FUN/INF labels as final truth
- hiding raw FUN/INF behind friendly names

## UI Direction

Desktop UI should feel like a serious engineering cockpit with the same mature engineering product identity used by the desktop tool:

- clean, modern, calm, professional
- soft white cards, restrained blue accent, slim scrollbars, segmented tabs, modern DataGrid rows
- no giant SaaS hero typography inside the app
- no noisy focus rectangles, raw default controls, heavy black borders, or cramped engineering panels
- readable grids and cards
- restrained accent colors
- clear separation: Setup / Master Control / Value Viewer / Relay Event Log / Monitor / Findings / Report
- evidence-rich, not decorative

When improving visuals, keep protocol correctness and render performance intact. Do not create expensive animations or unbounded visual trees for high-volume polling views.

## Output and Performance Rules

User-facing output must be operator-first, not raw-hex-first.

Required hierarchy:

```text
Operator Evidence  -> readable meaning/action for normal users
Value Viewer       -> current relay snapshot
Relay Event Log    -> relay timestamp edge/state-change events
Frame Trace        -> advanced raw hex/protocol transparency
```

Raw hex must stay visible, but it belongs in Frame Trace, inspector panels, and report evidence. Do not make raw hex the main language of the application.

For high-volume polling:

- do not render one WPF row synchronously per incoming frame
- use UI queues and timed batch flushes
- keep visible tables bounded
- keep engine retained evidence bounded while counters continue to represent full session totals
- Value Viewer must remain a snapshot, not an append-only log
- Relay Event Log must remain edge/state-change oriented

Update `docs/OUTPUT_AND_PERFORMANCE_POLICY.md` when changing output, buffering, retention, or rendering behavior.


## Stop / Cancellation Rule

Desktop Stop must never leave Start and Stop both disabled.

Required behavior:

- Stop cancels the active session token.
- Stop requests active transport close so blocking serial reads/writes are released.
- UI must restore Start enabled after the session finalizer completes.
- If a serial driver blocks cancellation, the UI must expose a force-close path rather than trapping the user in a disabled state.

Do not rely only on `BaseStream.ReadAsync` cancellation for USB/RS485 serial devices. Serial read paths should use configured timeouts and tolerate transport close during shutdown.


### Responsive layout and header stability rule

The WPF cockpit must behave like a mature web app layout: workspace-first, no clipped controls, no jittering Auto-sized headers, and no raw WPF/default-looking artifacts. Keep high-frequency protocol changes out of top summary cards. The top session state card must show stable session phase only (`Idle`, `Running`, `Stopping`, `Stopped`, `Completed`, `Faulted`, or critical `Attention`). Detailed per-frame state belongs in Operator Evidence / Frame Trace.

All large tables must have virtualization, recycling, and explicit scrollbars. Do not create unbounded UI collections or per-frame synchronous rendering. When changing layout, update `docs/RESPONSIVE_LAYOUT_POLICY.md`.

## Documentation Must Stay Updated

When changing behavior, update at least one of:

- `README.md`
- `docs/ARCHITECTURE.md`
- `docs/MASTER_POLLING_POLICY.md`
- `docs/EVENT_LOG_POLICY.md`
- `docs/MAPPING_PROFILE_SCHEMA.md`
- `docs/ROADMAP.md`
- `docs/OUTPUT_AND_PERFORMANCE_POLICY.md`
- `docs/RESPONSIVE_LAYOUT_POLICY.md`
- `docs/RELEASE_PACKAGING.md` when changing release scripts or GitHub release assets
- `docs/VALIDATION_MATRIX.md` when adding relay/simulator validation evidence
- release notes for the new version


## Release Packaging Rule

Release assets must be reproducible. When changing project output, update `scripts/publish-windows-portable.ps1`, `scripts/verify-release-package.ps1`, `.github/workflows/release-package.yml`, and `docs/RELEASE_PACKAGING.md` when needed. Do not publish only source ZIPs for user-facing releases; provide a Windows portable package and checksum.

## Future Coding Priorities

1. Hardening active master engine
2. User mapping profile editor/import/export
3. Value Viewer and Relay Event Log correctness
4. Evidence report quality
5. Real relay interoperability testing
6. AutoTest/scenario runner
7. Optional generic slave simulator later

Do not prioritize cosmetic UI work over master polling correctness and evidence integrity.


## Assessment rule

AutoTest/assessment code must summarize evidence without hiding raw frames. It may classify Pass/Warning/Fail, but it must keep the evidence and recommendation visible. Assessment must not invent vendor semantics or signal names; mapping still comes only from user profile.

## Diagnostics and exception handling rule

Recoverable runtime exceptions must never be left as unhandled UI faults. Convert them into Diagnostics evidence rows with severity, source, code, message, recommendation, and full exception detail. Diagnostics rows must remain selectable and copyable for escalation, similar to Visual Studio Error List behavior. Normal serial no-data timeouts are protocol/runtime conditions, not fatal exceptions.


## v1.2.4 Addendum - Slave Simulator Boundary

- The IEC-103 slave simulator is a deterministic test harness for validating the master engine.
- It must not pull the product away from the primary direction: active IEC-103 master tester + analyzer.
- Slave simulator behavior must remain deterministic, bounded, and useful for regression testing.
- Mandatory simulator behaviors: ACK reset/clock/GI, ACD=1 while Class 1 queue exists, Class 1 drain, GI END when enabled, NO DATA when empty.
- Negative modes are allowed only when explicit: silent, DFC busy, bad checksum, missing GI END.
- Do not hardcode vendor signal names in the simulator. Use generic FUN/INF values and keep user mapping profile as the only source of signal names.


## v1.2.5 layout/diagnostics note

- Keep operator views readable first; raw hex stays in Frame Trace and Inspector.
- Wide protocol columns, horizontal scroll, ellipsis, and tooltip are preferred over visually clipped text.
- Serial close/dispose driver exceptions must be converted into Diagnostics evidence, not allowed to interrupt Stop/Close workflow.

## WPF XAML compatibility guard

- Do not use WinUI/UWP-only properties in WPF XAML.
- Do not set `FrameworkElement.Resources` via a `Style` setter for DataGrid-wide cell text styling; define explicit keyed styles or column element styles instead.
- Any XAML visual polish must be compile-safe first, then polished.

## v1.2.7 UI output guardrail

- Do not make raw hex the main workflow language. Raw frame data belongs in Frame Trace and selected-row evidence.
- Frame Trace must not contain empty raw rows or generic STATE notes. It should show real TX/RX protocol frames only.
- Operator Evidence should suppress repetitive normal Class 2 polling noise. Show meaningful activity: startup, GI, reset, ACD event-drain, relay values, edge events, timeout, DFC, warnings, and faults.
- Primary grids should avoid routine horizontal scrolling. Use fewer columns, star sizing, selected-row inspectors, and report appendices for long detail.
- Text fields in the connection panel must remain readable when enabled or disabled. Do not use disabled opacity that makes numbers unreadable.


## Slave simulator behavior guardrail

The IEC-103 slave simulator is a deterministic test environment for the master engine. It must remain protocol-correct:

- do not push unsolicited bytes without a master poll,
- raise ACD=1 only when Class 1 data is pending,
- send Class 1 event data only after Request Class 1,
- return NO DATA when the requested class has no data,
- use Class 2 for background measurands only when no Class 1 queue is pending,
- keep protection pickup/trip events latched until command reset or auto reset,
- keep relay timestamp as the event-log source time.

Simulator signal names must remain in user-editable sample mapping files, not hardcoded as official vendor semantics.

## v1.2.9 UI output direction

The WPF shell now uses a more compact AR-style header, lighter premium typography, subtle gradient cards, and a commissioning-first Frame Trace grid. Raw hex remains preserved as selected-frame evidence, while the primary grid prioritizes readable engineering meaning.


## WPF typography and frame-trace UX rule

Use an Aptos-centered modern font stack and avoid heavy semibold/bold styling except where a status number truly needs emphasis. Header cards and KPI cards must stay compact. Frame Trace must explain protocol meaning first and keep raw hex as expandable/secondary evidence. Long protocol explanations must wrap/scroll inside a bounded inspector, never clip invisibly.


## v1.2.11 update

- Frame Trace inspector now uses a meaning-first raw hex map. Hovering hex blocks explains the selected byte/field so users can understand the frame without reading raw hex manually.
- Session state and sidebar spacing were tightened to avoid clipping and hover cut-off.

## Frame Trace / Hex Inspector rule

Frame Trace must explain protocol meaning first. Raw hex should be grouped into protocol-aware blocks (FT1.2 envelope, control/link, ASDU header, FUN/INF, payload/time/value, checksum/end). Hovering raw hex must highlight the matching meaning row without replacing panel content or causing flicker. Do not revert to isolated single-byte explanation as the primary behavior.

## Left panel spacing rule

Buttons, dropdowns, and text fields in the left command panel need safe margins because hover/shadow effects must not be clipped by the card edge or scroll viewer boundary.


## Line Monitor Pro guardrail

Frame Trace must behave like a protocol-aware line monitor, not a single-byte dictionary. Raw hex must be grouped by IEC-103/FT1.2 structure and synchronized with stable interpreter blocks. Hover/click may highlight matching groups, but must not rewrite the inspector content, resize the panel, or create flicker. Raw hex is audit evidence; readable protocol meaning is the primary workflow.


## v1.2.15 UI rule
- Line Monitor rows include a compact `Frame` column for raw hex evidence.
- The selected-frame interpreter must be readable as compact protocol cards, not a nested scrolling text wall.
- Raw hex grouping remains field-aware: FT1.2, control/link, ASDU header, FUN/INF, value/time, integrity.

## v1.2.16 Line Monitor Rule

Frame Trace must behave like a real IEC-103 line monitor: TX/RX transaction tracking on the left and selected frame interpretation on the right. Do not return to a bottom-only interpreter or hover-only explanation. Raw hex remains visible as evidence, but the selected frame's IEC-103 meaning must be readable without scrolling away from the trace.



## v1.2.17 Instrument Cockpit Lock

- Main screen is a protocol observation cockpit, not a configuration form.
- Connection and polling parameters belong in the Setup overlay.
- Header uses compact chips and LED activity indicators, not large KPI cards.
- Default master session is continuous monitoring until Stop; optional timeout is a setup parameter.
- UI font must remain Aptos-only; do not use monospace fonts for raw hex.

### v1.2.18 UI/runtime guardrail
- All WPF DataGrid monitor surfaces must remain `IsReadOnly=True`; they are evidence views, not editors.
- Do not use text-only command rail buttons where an icon + tooltip is clearer.
- Line monitor must prioritize readable transaction tracking and avoid routine horizontal scrolling.


## v1.2.19 Grid and Data Output Rules

- All monitoring grids must remain read-only, multi-select, full-row selection grids.
- Do not allow DataGrid edit mode; runtime monitoring rows are evidence, not editable form data.
- Right-click copy must preserve tab-separated row data suitable for Excel/Notepad paste.
- `Export Data` exports the selected evidence grid as tab-separated `.txt`; do not export Session Notes through this button.
- Value Viewer order must remain stable during live polling: update an existing signal row in place; do not move it to the top.
- Value Viewer grouping order: digital/status/protection points first, measurands second, other/unmapped values last.
- Event Log must show SOE date and time, not only time-of-day.
- Typography rule: Aptos only. Use normal weight by default; reserve medium weight for signal address, value data, relay timestamp, and high-value counters only.
- Frame interpreter should not include `Copy raw` / `Copy decode` buttons; copying belongs to grid context menu or selected-tab export.


## Class 1/Class 2 audit rule
- Line Monitor `Class` means transaction/request class, not ASDU type.
- Class 1 drain must be ACD-driven or bounded GI follow-up only.
- Class 2 remains normal background polling.
- ACD=1 on a Class 2 response is an advertisement of pending Class 1 data, not a reclassification of that response.

## Event Log rule
- SOE time must use relay timestamp when available.
- If no relay timestamp exists, show exactly `no timestamp`; do not synthesize a fake date/time.
- Event Log must support All / Digital status / Analog filtering.

### v1.2.21 UI motion and button palette
- Segmented navbar uses a bright fluid pill treatment, not a grey selected state.
- Hover grows subtly and click compresses subtly to make navigation feel tactile.
- Primary buttons use a brighter modern blue/cyan accent with dark readable captions.


## XAML safety note v1.2.22

Do not use Button-specific dependency properties such as `IsPressed` in `TabItem` styles. TabItem motion should use WPF-safe hover/selected triggers or code-behind VisualState handling.


## v1.2.23 UI cockpit guardrail

- Use the custom segmented navbar with a real sliding pill; do not rely on default TabItem selected gray highlight.
- Left rail buttons must use icon + small caption with safe hover/shadow margins.
- ComboBox controls must use the modern rounded web-style template and stay visually aligned with TextBox fields.
- Do not use invalid WPF control properties such as TabItem.IsPressed.
