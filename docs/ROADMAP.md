# ARIEC60870 Product Roadmap

ARIEC60870 is a focused IEC 60870 Evidence Analyzer for authorized relay, RTU, gateway, FAT, SAT, commissioning, and troubleshooting workflows.

The product direction is evidence-first: run or reopen an IEC 60870 communication session, keep the decoded evidence readable, preserve the raw frame trail, and export a professional PDF report that can be reviewed outside the application.

## Product position

ARIEC60870 should be presented as:

- an IEC 60870-5-101 / 103 / 104 evidence analyzer;
- a commissioning and troubleshooting assistant;
- a lightweight FAT/SAT evidence capture and report tool;
- a protocol trace reader with IEC-101, IEC-103, and IEC-104 awareness.

ARIEC60870 should not be presented as:

- a production SCADA system;
- a redundant master station;
- a gateway or RTU replacement;
- a certified conformance test suite;
- a universal protocol test-set replacement.

This keeps the public message honest while still making the project useful to protection, automation, gateway, and commissioning engineers.

## Current maturity snapshot

| Area | Current maturity | Public-readiness note |
|---|---:|---|
| IEC 60870-5-101 workflow | Medium | usable for guided serial endpoint checks, startup evidence, GI, polling, values, and command lifecycle review |
| IEC 60870-5-103 workflow | Medium | usable for relay communication review and IEC-103 evidence visibility |
| IEC 60870-5-104 workflow | Medium | usable for authorized TCP endpoint checks and APDU/APCI visibility |
| Protocol Trace | Medium-high | useful as a raw evidence workspace; must remain stable under live traffic |
| Operator Evidence | Medium-high | readable summary rows for session review and reporting |
| Value Viewer | Medium | useful for current values; filtering and point database maturity remain future work |
| Event Log | Medium | useful for events and diagnostics; grouping can still improve |
| Command Dock | Medium | useful for controlled checks; should continue to improve lifecycle evidence and operator safety |
| `.ariec` capture package | Medium-high | important portable evidence artifact; manifest and replay behavior should remain central |
| Offline capture review | Medium | should rebuild all relevant workspaces from one source of truth |
| Native PDF evidence report | Implemented baseline | built-in clean-room PDF engine is available; future work is PDF/A, richer typography, branding, and appendix controls |
| Release automation | Medium-high | single-file Windows ZIP, checksum, SBOM, and provenance workflow are in place |
| Passive monitor / NUC evidence | Planned | future feature family; should start with offline IEC-104 PCAP review before live capture |
| RTU/slave simulation | Future | useful later, but not required for the current public positioning |

## Public-show strategy

The strongest public story is intentionally narrow:

> Download one Windows ZIP, run ARIEC60870.exe, connect to an authorized IEC 60870 endpoint or open a saved capture, review evidence, inspect raw frames, and export a professional PDF report.

The first public demo should show:

1. opening the app without build tools or launch scripts;
2. configuring IEC-101, IEC-103, or IEC-104 settings from an approved test profile;
3. receiving GI, value, event, diagnostic, and command lifecycle evidence;
4. reviewing Operator Evidence, Value Viewer, Event Log, Frame Trace, Diagnostics, and Report;
5. exporting a native PDF evidence report;
6. preserving sanitized screenshots and sample profiles only.

## Release gates

### Gate A — Internal demo build

Suitable for controlled demonstrations and screen recordings.

Required:

- Windows package starts from `ARIEC60870.exe` without opening the source project.
- Protocol Trace and Operator Evidence are readable during a live session.
- Setup, command, value, event, frame trace, diagnostics, and report workspaces are available.
- `.ariec` capture opens and rebuilds evidence views.
- Native PDF report export works on a session or opened capture.
- Screenshots and examples use sanitized data.

### Gate B — Public preview build

Suitable for GitHub public release and internal/external engineering demo.

Required:

- README and Quick Start explain the evidence workflow in user language.
- Landing page and GitHub Pages do not contain stale report-export or internal-workflow wording.
- Screenshots show the mature product workflow, not debug views.
- Release assets include a user ZIP, checksum, SBOM, and provenance.
- Build/test/Pages/release workflows are green on the default branch.
- Known limitations are documented honestly.

### Gate C — Field-ready evidence build

Suitable for repeated FAT/SAT and troubleshooting use.

Required:

- PDF report includes project/session metadata, selected evidence rows, raw appendix, and hash.
- `.ariec` capture includes manifest, frames ledger, hash, and retention notes.
- Command lifecycle evidence is clear: select, execute, ACTCON, ACTTERM, feedback IOA, timeout.
- GI completeness and IOA coverage matrix are report-ready.
- Session save/reopen behavior is predictable for multi-day FAT/SAT work.
- Release artifacts are verified on a clean Windows machine before publishing.

## Near-term roadmap

### v3.6.x — Public polish and release discipline

Goal: keep the current evidence analyzer stable and professional before expanding scope.

Planned:

- keep all public wording aligned around **ARIEC60870 Evidence Analyzer**;
- keep GitHub Pages and `/docs` compatibility mirror synchronized;
- keep release automation green for single-file Windows ZIP, checksum, SBOM, and provenance;
- keep native PDF report wording consistent across app, README, docs, landing page, and release notes;
- close or regenerate stale Dependabot pull requests created by older automation settings;
- validate the release package on a clean Windows machine before marking it stable.

Exit criteria:

- a new user can download, run, understand the workflow, and export a PDF report within minutes;
- the default branch looks green and coherent on GitHub;
- no active public docs describe already-implemented features as still planned.

### v3.7.x — Capture replay and review mode

Goal: make `.ariec` capture a first-class review artifact.

Planned:

- open `.ariec` in offline review mode;
- rebuild Protocol Trace, Operator Evidence, Value Viewer, Event Log, Diagnostics, and Report from one ledger;
- show capture manifest in a readable panel;
- verify hash on open;
- show capture source, duration, frame count, and report readiness;
- allow re-export from opened capture.

Exit criteria:

- capture files behave like portable evidence sessions;
- reopening a capture is predictable and visually clear.

### v3.8.x — Trigger and pre/post capture

Goal: capture important events automatically without pretending to automate a full official FAT/SAT procedure.

Planned:

- trigger rules for CA, IOA, Type ID, COT, quality, command confirmation, command termination, feedback, timeout, and CA mismatch;
- pre-trigger and post-trigger frame windows;
- triggered-capture marker in Operator Evidence;
- export trigger result as `.ariec` and native PDF report;
- user-configurable trigger presets.

Exit criteria:

- field troubleshooting can capture what happened before and after important protocol events;
- triggered evidence can be separated from ordinary live-session noise.

### v3.9.x — Task Mode Lite

Goal: guide repeated IEC-101/103/104 actions while keeping the engineer in control of the actual test procedure.

Planned task cards:

- connect and link reset;
- General Interrogation;
- Class 1 / Class 2 observation;
- read selected IOA;
- clock synchronization;
- digital command lifecycle;
- analog setpoint lifecycle;
- SOE observation;
- link disconnect/reconnect observation.

Design principle:

- the app guides and captures evidence;
- the engineer/vendor still controls physical conditions, RTU settings, simulator settings, and official capture timing.

Exit criteria:

- user can run a guided task and export task evidence;
- failed attempts and successful official captures can be separated.

### v4.x — Passive monitor and NUC evidence family

Goal: add read-only monitoring and redundancy evidence without changing the current product into an unsafe sniffer.

Planned sequence:

1. offline IEC-104 PCAP/PCAPNG review;
2. live IEC-104 passive monitor for authorized mirror/TAP/endpoint capture;
3. offline IEC-101 serial byte-stream review;
4. live serial passive monitor with explicit tap mode and safety warnings;
5. NUC / redundant-link evidence summary and PDF section.

Safety position:

- passive monitor mode must be read-only;
- live Ethernet capture requires a valid observation point such as SPAN/mirror port, TAP, endpoint host, or firewall/router capture;
- serial passive monitor requires appropriate RS-232/RS-485 tap wiring and must not drive the line.

## Later roadmap

### IEC 60870 point database maturity

- import/export point list;
- IOA naming and grouping;
- raw/scaled value conversion;
- quality visualization;
- point mapping validation;
- command feedback mapping.

### RTU/slave simulation foundation

- simple IEC-101/104 outstation profile;
- point model;
- GI response;
- Class 1/2 response;
- digital/analog state changes;
- basic command response.

### Advanced comparison and validation

- compare two captures;
- compare observed evidence with expected point list;
- generate difference report;
- annotate rows for FAT/SAT acceptance notes.

## Product principles

1. **Evidence first**  
   Every major action should be reviewable, exportable, and reproducible.

2. **User-owned data**  
   Mapping profiles, point names, captures, and project metadata belong to the user and should be easy to sanitize.

3. **No hidden magic**  
   Decoded protocol fields, raw hex, and assumptions should remain visible.

4. **Safe wording**  
   The product should be described as a focused IEC 60870 Evidence Analyzer until simulator, task mode, passive monitor, NUC, and report workflow become mature enough for a wider claim.

5. **Public trust**  
   Apache-2.0 licensing, clean release assets, checksums, SBOM, provenance, clear docs, and green CI matter as much as feature count.
