# PRD — IEC-101 Passive Serial Monitor & Forensic Investigation Workspace

Status: Design baseline / not implemented  
Date: 2026-10-27  
Repository baseline: master 52777a7858d28b53c0739112219ac7685008ec84  
Owner issue: #19  
Companion: docs/IEC101_PASSIVE_MONITOR_ARD.md and docs/IEC101_PASSIVE_MONITOR_UX.md

## 1. Why this exists

Field investigation: SICAM A8000 ↔ ETERA SCADA over IEC 60870-5-101 serial with two redundant paths. Operators report active-path switching about every 30 seconds; standby appears to exchange only Link Status traffic. The investigator owns two COM ports and intends to tap both transmit wires of one production RS-232 full-duplex path without disconnecting either endpoint.

**Success is not just “messages appear.”** It is an unmodified production exchange captured with raw evidence, directional decoding, observed request/response pairing, error history and a defensible before/after timeline around a switching event, saved to an analysis bundle that another engineer can reopen.

## 2. Reference benchmark: ASE2000 Version 2

Publicly documented capabilities to benchmark independently, **not** code/assets/UI to copy:

| ASE2000 V2 public behavior | ARIEC60870 product requirement | Scope |
|---|---|---|
| Line Monitoring uses both serial COM ports to listen to master/RTU directions | Physical direction mapping with two receive-only COMs | P0 |
| Line Monitor raw+interpretive and clickable linked fields | Combined decoded trace + inspectable byte/field spans | P1 |
| Messages view and line-oriented monitoring | Dense chronological table and detailed inspector | P1 |
| IEC-101 interoperability field sizes must be configured | Link address / COT / common address / IOA widths, validate or show unknown | P1 |
| Communication statistics and display filters | Per-port byte/frame/error counters, non-destructive filters | P1 |
| Captured sessions and offline retrieval | Whole-session durable raw archive plus reload/replay | P0–P1 |
| Event-trigger capture, event logging | Evidence-linked fault markers, pre/post investigation windows | P2 |
| Point Values view | Reuse existing point/value models; unknown IOAs remain raw | P2 |
| Line Analyzer carrier/bit waveform with vendor hardware | **Not equivalent** using ordinary COM adapters; no invented carrier timing | Excluded unless suitable hardware |

Sources (behavior/reference only):
- Manufacturer: https://www.ase-systems.com/products/ase2000-v2/
- Manufacturer V2 manual: https://www.ase-systems.com/wp-content/uploads/2021/06/ase2000-communication-test-set.pdf (sections 6, 7.3, 8, 13, IEC-101 Quick Start).
- Manufacturer knowledge base: https://www.ase-systems.com/ase2000-v2-knowledge-base/

Clean-room rule: use published concepts and standard protocol rules to design our own algorithms. Do not copy ASE GUI layouts, algorithms, manuals, screenshots, or proprietary capture formats into the project.

## 3. Personas and primary jobs

- Commissioning engineer: select two tap COM ports, choose direction and serial parameters, start observing within a few actions, capture the ~30-second switching cycle without modifying the plant.
- Protocol engineer: drill from request to response, inspect PRM/FCB/FCV/ACD/DFC, link address, Type/COT/CA/IOA, correlate errors/retries and see raw proof.
- Remote collaborator: open an exported .ariec package, verify integrity, replay locally, compare findings without connected hardware.

## 4. Invariants (MUST)

1. Passive mode is read-only by design and on the wire. It MUST NOT instantiate active Master/Slave sessions, send bytes/Break, run GI, transmit link probes, or assert modem control lines onto the plant.
2. Electrical isolation is a hardware property, not a software checkbox. Monitor TX/RTS/DTR outputs are not connected to production; use an approved high-impedance or isolated tap. Do not remove required RTS/CTS/DSR/DTR/DCD or other end-to-end wires.
3. **Two COM = both directions of one serial link, not two redundant links. Four COM = two directions for each of redundant links A and B.** Capture topology must say which is actually installed.
4. Local COM opened ≠ link healthy ≠ SCADA active link. Distinguish capture status, observed protocol exchanges and inferred redundancy role.
5. Preserve all received raw bytes (including noise, malformed frames and single-character responses); rendering filters, row limits and user searches may not reduce archived data.
6. If OS/driver bytes are lost or the app cannot keep up, mark integrity incomplete or uncertain; never claim absolute wire completeness from software counters alone. Capture all detectable driver errors, framing/parity/overrun flags and file/queue failures.
7. A single tap pair cannot prove a switch to unmonitored Link B. Link A polling cessation is an observation, not proof of ETERA's internal failover decision.
8. Standby link exchanging only Link Status may be normal; no automatic alarm on status-only without supporting evidence.
9. Request/response timing must include timebase, acquisition granularity and uncertainty. Windows USB COM receipt time is not hardware bit-edge time.
10. Reopenable exports never depend on WPF visible rows, currently selected tab, or existing ARIEC_CAPTURE_V1 trace suppression.
11. Do not export confidential field evidence to public repo, issue comments or CI logs.

## 5. Functional requirements with verifiable acceptance IDs

### FR-01 Mode and setup (P0)
- New workflow label: “IEC-101 Passive Monitor”, separate from “IEC-101 Master” and “IEC-101 Dual Link Redundancy” active controllers.
- “Single serial link / 2 COM” preset first; “Redundant links / 4 COM” available later.
- Each COM labeled by physical source: “Master TX (SCADA → RTU)” versus “RTU TX (RTU → SCADA)”; role swapping supported before capture and recorded in manifest.
- Validate duplicate COM names, both ports open/failed, serial parameters for each side (baud, data bits, parity, stop bits), link address size, COT width, CA width, IOA width and output folder.
- IEC-101 size profile: explicit widths with optional guarded trial decode using bounded candidate profiles; never silently assert a guessed setting as verified.
- Start is blocked on failed preflight. No master command dock in this mode.
- Acceptance: A test double that exposes only receive operations can start/stop an observing session without any MasterSession creation. Repeated start/stop leaves zero leaked handles.

### FR-02 Dual-direction byte acquisition (P0)
- Acquire each port independently. Tag every chunk with source port, sequence, monotonic receive tick and UTC mapping.
- Append raw records immediately to a crash-recoverable disk spool; acquisition must not wait on decoder or WPF.
- Retain all bytes including E5/A2, 10 fixed, 68 variable, stray bytes, invalid length, bad checksum, partial trailing bytes. No “discard until valid start byte” in the archival path.
- Record driver exceptions and errors; introduce explicit “integrity uncertain” when the driver cannot prove absence of earlier loss.
- Surface live byte/queue/disk/driver health and a stable “monitoring” status.
- Acceptance: deterministic two-port fixture file hash/byte sequence matches recorded raw chunks; no synthetic transport Write invocation; injected failure marks session partial.

### FR-03 IEC-101 interpretation (P1)
- Per-direction incremental FT1.2 reassembly; keep partial frames across reads, resynchronize on corruption without deleting raw.
- Decode link functions, PRM, FCB, FCV, ACD, DFC and link address; decode ASDU Type/COT/CA/IOA/value/quality/timestamps using a shared IEC-10x ASDU decoder migrated safely from Master to Core (not the existing IEC-103 Core.AsduDecoder).
- Single-character ACK/NACK needs contextual direction and a qualified correlation; report “unpaired/ambiguous” when context insufficient.
- Expose raw and decoded fields with byte offsets/highlighting, similar **in function** to ASE2000's linked data display.
- Acceptance: valid/corrupt/short/back-to-back/noise stream fixtures have exact byte coverage; unsupported fields remain “unknown + hex”, never fabricated.

### FR-04 Evidence table and view filtering (P1)
- Chronological merged trace with link, physical direction, time, function, link address, Type/COT/CA/IOA, raw hex and flags.
- Dense virtualized grid; inspector on selection, keyboard find, copy row/hex, filter by direction, link, error and protocol fields, autoscroll toggle, freeze VIEW.
- “View paused” never means disk capture paused. Show disk truth counters even when UI is frozen.
- Acceptance: no synchronous WPF work per byte; UI scrolling/filtering cannot change exported raw hashes.

### FR-05 Export, replay, integrity (P1)
- New versioned .ariec forensic bundle includes raw stream and indices, capture manifest/topology/serial parameters, decoded frames, diagnostics, findings and metadata.
- Keep current ARIEC_CAPTURE_V1 import working. A V1 file must never be mislabeled “lossless session.”
- Export whole session or bounded time window with clear disclosure; an incident window must contain raw byte offsets/provenance, not just rows.
- Validate checksums, file/record bounds, path traversal, decompression limits on reopen. A process crash still yields a clearly identified partial recoverable session.
- Acceptance: deterministic V2 round-trip byte-identical, V1 import unchanged, truncation/tamper cases produce clear errors and original spool remains recoverable.

### FR-06 Passive transaction analysis (P2)
- Observe status requests, reset-link, class 1/2 polls, data replies, ACK/NACK, GI ACT/ACTCON/ACTTERM, negative confirmations, spontaneous events, command attempts, busy/no-data.
- Pair requests to responses only when identity, order and timing support a plausible match. Unpaired or ambiguous transactions remain explicit.
- Show response-latency distribution (median/p95/max and outliers) with arrival timestamp limitations, retry count, FCB repeat patterns, DFC busy, checksum/framing diagnostics, silence periods, ASDU unexpected sizes/addressing.
- Acceptance: golden streams show correct correlation, no spurious ACK association, no changing observer state because a byte was missing.

### FR-07 Switching / redundancy investigation (P2–P3)
- Preset “Investigate recurring switch” with adjustable suspected period (initial 30 s), comparison windows (120 s before/after by default), and optional “mark observed switch” user action.
- On one captured link: label observed “application traffic stopped”, “status-only”, “resumed”, “unobserved counterpart” rather than asserting Link A→B switch.
- On fully captured redundant links: infer traffic-active vs supervision-only by protocol activity with evidence IDs and confidence; not access to SCADA's internal controller state.
- Detect repeated intervals with jitter bounds and multi-cycle statistics, differentiate normal standby supervision from actual failed responses and traffic ownership changes.
- Acceptance: 30-second cyclic golden fixture with valid standby status, retry/gap and activity on second path yields correct markers; alternate fixture (healthy status-only standby) does not raise a false fault.

### FR-08 Engineer-to-engineer escalation (P2)
- Single “Export Investigation Package” action, combining raw archive + serial/port configuration + timestamp quality + findings + plain-language timeline + SHA256.
- The exported offline reader can generate shareable TSV/CSV, Markdown/PDF summary without making the PDF the sole evidence.
- Make human-entered device/firmware/cable/SCADA notes optional and label them “operator-supplied”.
- Acceptance: teammate can reopen offline, select a finding, jump to raw source and verify integrity.

## 6. UX acceptance and default first-use flow

See docs/IEC101_PASSIVE_MONITOR_UX.md. Primary workflow:
1. Choose “IEC-101 Passive Monitor” (not master).
2. Select preset: one link, two RX COMs.
3. Identify COM for SCADA TX and COM for SICAM TX; choose matching serial settings and protocol widths.
4. Confirm listen-only wiring/electrical checklist and capture folder.
5. Press “Start Recording”; both receiver indicators and archive bytes start updating.
6. Observe “Conversations”/“Trace” and mark suspected 30-second switch; keep recording through multiple cycles.
7. Press “Stop & Finalize” or “Export Investigation Package”; reopen capture to prove integrity.

Default density must prioritize the telegram list: avoid giant cards, oversized labels, multiple irrelevant master controls, and too many visible columns. Preserve existing restrained WPF visual design and virtualization policies.

## 7. Explicit non-goals

- Not a proxy, bridge, RS-232 repeater, test master, failover controller, telemetry injector, or replacement for ETERA.
- No automatic changes to A8000 configuration or SCADA settings.
- No inference of RTS/CTS/DTR/DSR/DCD from RXD alone.
- No bit waveform, electrical noise amplitude, modem carrier plot, or sub-millisecond physical timing claim from generic USB COM.
- Not a claimed full ASE2000 replacement for other protocols or its proprietary BCOM-USB hardware diagnostics.
- No exposure of business/project-specific ETERA assumptions as IEC normative requirements.

## 8. Release gates / completion

G0 Design: PRD + ARD + UX aligned with existing code and #19; explicit feasibility, hardware limitations, golden vectors and rollout boundaries.
G1 Capture: 2 COM RX-only with no output-connected lines; archive exactness on simulated and bench test. **Do not use on live production until approved cabling verified.**
G2 Decode/export: broken/noisy streams, integrity and replay, V1 regression and format migration.
G3 Investigation: 30-second and healthy-standby deterministic fixtures, false-positive analysis, analyst evidence reconstruction.
G4 Field: controlled real USB/RS-232 tap and live or bench A8000/ETERA evidence review, capture run ≥2 hours across target baud rates, zero application-detected drop in healthy bench run, explicit timestamp quality, no measured impairment of line behavior before/after tap.
G5 Release: Windows x64 runnable artifact verified and hash-pinned; no “field-proven” claims based on CI only.

Any loss found in G4 must produce non-green evidence quality, not a hidden log. Absolute physical-layer correctness requires specialized measurement hardware.

## 9. Decisions and unresolved field inputs

Locked: receive-only architecture; two COM per duplex path; V2 raw archive; separate workspace and engine; physical limitations must be disclosed; “observed vs inferred” distinction.

Required **before production field tap** (not blockers for implementation): actual SICAM communication module and ETERA serial-port wiring, electrical level RS-232 vs RS-485, cable pinout, flow control/handshake, serial framing, field link address lengths, confirmation of where both redundant paths terminate, and approval of high-impedance/isolated tapping.

## 10. Tracking

- Master design issue: https://github.com/masarray/ARIEC60870/issues/19
- This is a design deliverable; no passive capture implementation, CI proof or live commissioning validation is asserted.
