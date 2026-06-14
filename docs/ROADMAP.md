# ARIEC60870 Product Roadmap

ARIEC60870 is being shaped as a focused IEC 60870 evidence analyzer for relay, RTU, gateway, FAT, SAT, and field troubleshooting workflows.

The near-term goal is not to claim a universal protocol test-set replacement. The near-term goal is to deliver a public-ready IEC 60870 tool that is easy to open, easy to read, easy to capture evidence from, and credible enough to demonstrate in front of protection, automation, vendor, and commissioning engineers.

## Product direction

ARIEC60870 is built around one practical field problem:

> Engineers need a clear, reviewable, and portable way to prove what happened on an IEC 60870 communication session.

The product direction is therefore evidence-first:

- readable protocol trace,
- operator-readable evidence summary,
- value/event/diagnostic workspaces,
- unified `.ariec` capture files,
- offline capture review,
- exportable evidence,
- guided test workflow for repeated FAT/SAT work.

## Positioning

ARIEC60870 should be presented as:

- an IEC 60870 protocol evidence analyzer,
- a commissioning and troubleshooting assistant,
- a lightweight FAT/SAT evidence capture tool,
- a protocol trace reader with IEC 101/103/104 awareness.

ARIEC60870 should not yet be presented as:

- a complete universal protocol test set,
- a full replacement for mature commercial protocol suites,
- a certified conformance test tool,
- a complete RTU/slave simulator suite.

This keeps the public message honest while still making the product valuable.

## Current maturity snapshot

| Area | Current maturity | Public-readiness note |
|---|---:|---|
| IEC 60870-5-101/104 master workflow | Medium | usable for guided connection, GI, polling, values, and command workflow proof |
| IEC 60870-5-103 analyzer workflow | Medium | usable as protection relay communication analyzer |
| Protocol Trace | Medium | core workspace; selection, hold/resume, readability, and export must be polished before public demo |
| Evidence Summary | Medium | readable card view with unified evidence capture; still needs final stability polish |
| Value Viewer | Medium | useful for current values; needs filtering and point database maturity |
| Event Log | Medium | useful; needs clearer event grouping and export workflow |
| Command Dock | Medium | useful; needs safer operator workflow and clearer lifecycle evidence |
| `.ariec` capture package | Medium-high | already important; must become the central evidence artifact |
| Offline capture review | Medium | should rebuild all relevant workspaces from one source of truth |
| PDF evidence report | Low | required for public impact; planned next |
| Trigger / pre-post capture | Low | required for professional troubleshooting |
| Task Mode / guided test plan | Low-medium | should come after line monitor and evidence package are stable |
| RTU/slave simulation | Low | future phase, not required for first public show |

## Public-show strategy

The first public version should be intentionally narrow and polished.

The strongest public story is:

> Open an IEC 60870 session, watch the communication in a readable protocol trace, select important evidence rows, export a portable capture file, reopen that capture, and generate a clean evidence package.

The first public demo should show:

1. connect to IEC 101/104/103 source or demo/session replay,
2. receive GI/value/event/command evidence,
3. read Protocol Trace without the workspace jumping,
4. read Evidence Summary without table clutter,
5. select multiple evidence rows from Protocol Trace or Evidence Summary,
6. export selected `.ariec` evidence capture,
7. reopen `.ariec` and show the same data reflected across workspaces,
8. export a readable evidence report.

## Release gates

### Gate A — Internal demo build

This build is suitable for controlled demonstrations and screen recordings.

Required:

- Protocol Trace is default workspace.
- Protocol Trace has stable viewport behavior.
- Evidence Summary is readable without a bottom inspector.
- Multi-select works in Protocol Trace and Evidence Summary.
- Right-click export works from both workspaces.
- `.ariec` capture opens and rebuilds Protocol Trace and Evidence Summary.
- No obvious visual jumping while user is reading or selecting.
- Portable Windows ZIP runs without opening the source project.

### Gate B — Public preview build

This build is suitable for GitHub public release and LinkedIn/product demo.

Required:

- Follow Live / Reading Hold / Jump Latest workflow is explicit and easy to understand.
- Pending-frame counter is visible when live rendering is held.
- Export selected capture file is reliable.
- Export selected evidence text is reliable.
- Basic PDF evidence export is available.
- README and Quick Start explain the evidence workflow in user language.
- Screenshots show the mature product workflow, not internal debug views.
- Known limitations are documented honestly.

### Gate C — Field-ready evidence build

This build is suitable for repeated FAT/SAT and troubleshooting use.

Required:

- Evidence PDF has project/session metadata, selected evidence rows, raw appendix, and hash.
- `.ariec` capture has manifest, frames ledger, hash, report, and retention notes.
- Event trigger engine supports selected IOA/COT/TypeID/command feedback conditions.
- Pre/post capture windows are available.
- Command lifecycle evidence is clearer: select, execute, ACTCON, ACTTERM, feedback IOA, timeout.
- GI completeness and IOA coverage matrix are report-ready.
- Session save/resume is available for multi-day FAT/SAT work.

## Near-term roadmap

### v3.5.0 — Professional Line Monitor UX

Goal: make Protocol Trace and Evidence Summary stable, readable, and selection-friendly.

Planned:

- Follow Live toggle.
- Reading Hold state.
- Jump Latest action.
- Pending new-frame counter.
- Stable viewport during active communication.
- No visual movement while rows are selected.
- Resume Live action from toolbar and context menu.
- Better selected-row status: selected count, source workspace, export actions.
- Keep incoming data in stores while visual rendering is held.
- Apply the same hold/resume model to Protocol Trace and Evidence Summary.

Exit criteria:

- User can read rows during active communication without the workspace moving.
- User can select evidence rows with click, Shift, Ctrl, drag, and right-click.
- Selection is not lost when new data arrives.
- Export capture from selection works from both workspaces.

### v3.6.0 — Evidence Package Export

Goal: make exported evidence usable without reopening ARIEC.

Planned:

- Export selected evidence as PDF.
- Include project/session metadata.
- Include selected rows as clean formatted evidence cards.
- Include protocol metadata: Type ID, COT, CA, IOA, quality, timestamp, direction, raw hex.
- Include capture SHA256 and manifest summary.
- Include optional Markdown/text appendix.
- Add context menu item: `Export Selected Evidence PDF...`.

Exit criteria:

- User can right-click selected rows and produce a clean PDF evidence report.
- PDF is readable by non-ARIEC users.
- PDF includes enough metadata to support review and handover.

### v3.7.0 — Capture Replay and Review Mode

Goal: make `.ariec` capture a first-class review artifact.

Planned:

- Open `.ariec` in offline review mode.
- Rebuild Protocol Trace, Evidence Summary, and related diagnostic views from one `frames.jsonl` ledger.
- Show capture manifest in a readable panel.
- Verify hash on open.
- Show capture source workspace and row count.
- Allow re-export from opened capture.

Exit criteria:

- Capture file behaves like a portable evidence session.
- Reopening a capture is predictable and visually clear.

### v3.8.0 — Event Trigger and Pre/Post Capture

Goal: capture important events automatically without forcing full automatic testing.

Planned:

- Trigger rules for CA, IOA, Type ID, COT, quality, command confirmation, command termination, command feedback, timeout, and CA mismatch.
- Pre-trigger and post-trigger frame windows.
- Triggered capture marker in Evidence Summary.
- Export trigger result as `.ariec` and PDF.
- User-configurable trigger presets.

Exit criteria:

- User can capture “what happened before and after” important protocol events.
- Field troubleshooting becomes faster and more forensic.

### v3.9.0 — Task Mode Lite

Goal: guide repeated IEC 101/103/104 actions without pretending every FAT/SAT item can be fully automated.

Planned task cards:

- Connect and link reset.
- General Interrogation.
- Class 1 / Class 2 observation.
- Read selected IOA.
- Clock synchronization.
- Digital command lifecycle.
- Analog setpoint lifecycle.
- SOE observation.
- Link disconnect/reconnect observation.

Design principle:

- The app guides and captures evidence.
- The engineer/vendor still controls physical conditions, RTU settings, simulator settings, and official capture timing.

Exit criteria:

- User can run a guided task and export task evidence.
- Failed attempts and successful official captures can be separated.

### v4.0.0 — Public Evidence Workflow Release

Goal: public product release focused on IEC 60870 evidence workflow.

Planned:

- polished landing page screenshots,
- release ZIP and checksum,
- quick-start guide,
- sample capture files,
- sample evidence PDF,
- clear limitations,
- public demo script,
- issue templates for protocol captures and field feedback.

Exit criteria:

- A new user can download, run, open a sample, understand the workflow, and export evidence within minutes.

## Later roadmap

### IEC 60870 point database maturity

Planned:

- import/export point list,
- IOA naming and grouping,
- raw/scaled value conversion,
- quality visualization,
- point mapping validation,
- command feedback mapping.

### RTU/slave simulation foundation

Planned:

- simple IEC 101/104 outstation profile,
- point model,
- GI response,
- Class 1/2 response,
- digital/analog state changes,
- basic command response.

### Advanced comparison workflow

Planned:

- compare two sessions,
- compare before/after vendor setting changes,
- identify changed CA/IOA/COT/TypeID behavior,
- export difference report.

## Product principles

1. Evidence first.
   Every major action should be reviewable, exportable, and reproducible.

2. Stable reading over flashy live movement.
   Live data is useful, but the user must be able to read and select evidence without fighting the UI.

3. Single source of truth.
   `.ariec` captures should use one ledger and reflect consistently across workspaces.

4. Guided, not fake-automatic.
   FAT/SAT often requires vendor setting changes and multi-day retries. ARIEC should guide the workflow and bind evidence, not pretend every item can run automatically.

5. Public claims must stay honest.
   The product should be shown as a focused IEC 60870 evidence analyzer until simulator, task mode, trigger engine, and report workflow become mature.

## Immediate priority

The immediate public-show priority is:

```text
v3.5.0 Professional Line Monitor UX
v3.6.0 Evidence PDF Export
v3.7.0 Capture Replay and Review Mode
```

These three milestones make ARIEC60870 demonstrable as a serious product before expanding into heavier simulation or automated task workflows.
