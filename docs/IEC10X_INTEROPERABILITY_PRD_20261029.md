# PRD — ARIEC60870 Professional IEC-101/103/104 Interoperability & Forensics

**Date:** 2026-10-29. **Status:** canonical **product-direction** requirements for all implementation threads, not a statement that all features are complete. Read [research](IEC10X_NUC_RESEARCH_20261029.md) and [ARD](IEC10X_INTEROPERABILITY_ARD_20261029.md). Verify live master, open PRs, checks and issue #39/#19/#30 before work; historical component PRDs must not override merged code.


> **ACTIVE R6-R9 PRODUCT CONTRACT — revision 2026-10-31.** [R6-R9 requirements](#r6-r9-authoritative-milestones-and-acceptance--2026-10-31) supersede historical R0-R4 DELIVERY ORDER, not prior IEC101/103/104 protocol safety requirements. Read [ARD](IEC10X_INTEROPERABILITY_ARD_20261029.md) and [AGENTS](../AGENTS.md). R5 source code is the starting implementation baseline, NOT evidence that these new requirements are complete.

## Vision

Deliver one reliable, fast, flexible Windows engineering product for IEC 60870-5-101, IEC 60870-5-103 and IEC 60870-5-104 FAT/SAT, commissioning and forensic analysis. An engineer must always understand **what bytes were observed, what the protocol says, what the RTU actually confirmed, what remains unknown, and which test comes next**. High interoperability is an outcome of independently validated profile alternatives and field fixtures, not an unsupported marketing assertion.

## Modes and claims

| Mode | Evidence-backed present scope | Future acceptance |
|---|---|---|
| IEC101 active unbalanced | Master, GI, class 1/2, events, commands | More interop/FAT and strict profile correctness |
| IEC101 active A/B unbalanced | Independent links, standby supervision, failover, event evidence | NUC101 policy alternatives and physical FAT |
| IEC101 passive | 2/4 RX zero-transmit journal, replay, transactions, timeline | End-to-end all-object report equivalence and electrical FAT |
| IEC101 balanced & NUC balanced redundancy | **Not automatically supported** | Separate state machine, synthetic/physical proof before enabling active TX |
| IEC103 active | Protection FUN/INF master | Byte-level inspector and vendor-private unknowns |
| IEC104 active | TCP/APCI + shared 10x ASDU | Session/sequence/host-takeover and byte inspector FAT |
| NUC101 / NUC104 | Research-selected optional interoperability profile | No conformance badge until validated against controlled convention/project checklist |

## Operators and UX

**Field engineer:** select/validate profile; connect or open verified capture; see immediate active/standby state, values, SOE, quality and meaningful findings; export accurate PDF. **Protocol analyst:** detailed FC/PRM/FCB/FCV/ACD/DFC, COT with P/N/TEST/OA, VSQ/SQ and each IOA/quality/byte, matched request/reply/command lifecycle, raw provenance and time uncertainty. **Reviewer:** reopen archive and verify claims with no site equipment.

Reuse the existing WPF Setup, Frame Trace, Values, Events, Findings, Commands, Redundancy (where applicable), Capture, Report. Keep premium **compact** typography and dense virtualized grid. No new big window, no decorative oversized cards, no heavy hover animation. One **physical frame per top-level trace row**, multi-object drilldown on selection. COT = one numeric column plus small NEG/TEST chips; Quality = one column with up to three badges, complete raw bits in inspector. Raw original HEX and copy action accessible from selected frame; stop/freeze-view must never pause raw archive silently.

## Requirement contract (each has an owner, tests and traceable proof)

### PROFILE-01 [P0] — Versioned interoperability profile
Support explicit Generic IEC101/103/104, optional historical NUC101 rev2.0, optional historical NUC104 rev1.2, and project custom profiles; source/version, strict supported mode and user override. Record physical link 0/1/2 octets where lawful, COT1/2, CA1/2, IOA1/2/3, originator, balanced/unbalanced, permitted Type/COT, field width, time, UART/TCP, GI-after-switch, class priority and scaling. Never force NUC globally, assume historical NUC means every optional extension is legal, or enable unsupported active transmission. Import/export with backward compatibility and original-vs-override detail.

**Pass:** incompatible widths/modes rejected before active TX; passive raw remains available and uncertain fields clearly unknown; profile diff links actual bytes. No false NUC compliance labels.

### TRACE-01 [P0] — Trusted protocol byte/bit interpretation
IEC101 FT1.2 original start/L/control/link/checksum; FC, PRM, FCB, FCV, ACD/DFC; E5 contextual, unexpected A2 raw; IEC101/104 Type, VSQ/SQ, COT code/P-N/TEST/OA, CA, each IOA/payload/quality/CP timestamps; 103 FUN/INF and relay semantics; 104 APCI I/S/U, N(S)/N(R), STARTDT/STOPDT/TESTFR and TCP identity. One authoritative parser result per byte range; malformed/unsupported fields never silently Good.

**Pass:** 0/1/2 legal link length, COT/CA/IOA combinations, SQ multi-object, malformed checksum/length and unknown Type round-trip with exact original hex and source offsets.

### EVENT-01 [P0] — Lossless observation and GI
Every valid spontaneous/return-information object is an event fact even if first observation or repeat; snapshots update Values without SOE spam unless a real state change is known. Preserve relay time, host arrival and validity independently. GI ACT/ACTCON/data/ACTTERM per-station CA with logical mapped coverage; unobserved/missing/partial explicitly labeled. Event-driven NUC101 application, class-2 no-data, optional project cyclic transfers must never trigger baseless alarm.

### QUALITY-01 [P0] — Source-backed flags by correct layer
Typed SIQ/DIQ/QDS/BCR, no new per-bit grid columns. Measurement QDS OV, counter BCR carry, link DFC, mapped queue overflow indication and UART overrun are distinct facts. Sender QDS OV remains authoritative even when transmitted value below signed maximum; no fabricated flag or device bug allegation.

### MEAS-01 [P0] — Signed raw, scaled engineering and quality
Decode normalized Type9, signed scaled Type11 and float Type13 plus time-tag variants. Show original little-endian octets, int representation, quality, profile scaling, mapped engineering units, conversion uncertainty. Include boundary synthetic fixtures +32755/+32763/+32767/-32768/-32767 and each OV=0/1. A sign-boundary transition is not automatically hardware overflow.

### CMD-01 [P0] — Full command-response truth
Show SELECT/EXECUTE, COT6/7/8/9/10, P/N negative, Type44–47 error, retries, uncertain overlaps and wrong-source/phase/CA/IOA responses. Feedback requires healthy quality and verified **expected target state**, not mere IOA presence. Link ACK, ACTCON and ACTTERM alone do not prove primary switchgear state. NUC101 permits feedback event and ACTTERM in either order. No passive TX. Active commands require explicit authorized user initiation with safe confirmation.

### RED-01 [P0/P1] — NUC-aware but non-assumptive redundancy
NUC101 **unbalanced** dual-link startup probes both, resets/polls chosen primary, status-supervises standby, resets/polls promoted link; protect independent FCB/queues, correlate per source. NUC101 **does not require GI after switching**, though project policy may. Show Disabled/Manual/OnFailover/ProjectRequired and report Missing only when selected policy expects it. **Balanced NUC** uses a different link-test/ACK/NACK and active-line procedure and is separate, future gated work. A 104 frontend takeover/GI rule is project specific, not serial NUC101 law. 2 RX vs 4 RX scope explicit; no assertion of inaccessible SCADA internal state.

### CAPTURE-01 [P0] — Forensic single source of truth
Append-only raw stream and typed immutable source IDs from capture to frame/ASDU/object/event/command/report. Hash/CRC verification, exact decode width and object offsets, error/loss/time provenance. Offline replay renders all objects into Trace, Events, Values, Findings and PDF identically. Legacy .ariec with missing fields remains readable raw and explicitly unverified. UI preview ring can be bounded; archived received bytes cannot be silently dropped.

### FINDING-01 [P1] — Smart but honest diagnosis
Finding has rule+profile edition, observed evidence refs, uncertainty and next test. Separate Observed Valid, Observed Unverified, Correlated, Inferred, Not Observed. A suspected root cause is not automatically proven. Never infer utility/operator state from a sole serial tap.

### UX-01 [P1] — Professional response and field usability
No repeated GUI parser or per-frame Dispatcher work; typed WPF selection inspector, small flag badges, source-aware A/B display, keyboard copy/filter, no giant bold header. Test 100/125/150% DPI, live 1200-bps immediate first trace, sustained streams, Start/Stop, freeze while disk journal continues and opening multi-day capture without full RAM load. Measure CPU, allocations, first paint, UI latency and journal loss.

### FAT-01 [P0-P2] — Interoperability proof matrix
Deterministic IEC101/104/103 synthetic vectors, independent bench sessions (multiple implementations), physical 2/4 RX serial and TCP, corruption/noise/replay, 101 link widths, NUC unbalanced A/B, balanced only once separately implemented, 104 sequence/session and frontend recovery, flag/numeric extremes, commands out-of-order, GI outcomes, long capture, DPI. Release mode-specific verdict Supported/Tested/Partial/Not Tested rather than universal certified claim.

## Historical R0-R4 delivery order (superseded in sequence by R6-R9)

**R0 document alignment:** this PRD/ARD/research + AGENTS; documentation only, no interoperability implementation claim. **R1 P0 safety:** reconcile current command-verdict and raw evidence integrity work, correct source-aware flags/measurements/replay with tests. **R2 profiles/GUI:** versioned generic vs NUC vs project profile and byte inspector parity 101/103/104. **R3 field/quality:** real multi-vendor FAT, reports parity and measured UI/CPU. **R4 release candidate:** reproducible tested Windows ZIP, hash/SBOM, truthful capability matrix.

No NUC certification, balanced active TX, production SCADA replacement, private capture upload, or competitor UI copying without separately approved scope.

## R3-B/C 2026-10-30 implementation checkpoint — EVENT-01 / CMD-01 / CAPTURE-01

R3-A (#59) is merged. R3-B/C continues with source-qualified GI conversation evidence, not a second WPF or parser engine. The Master-layer `Iec10xGiCasebook` keeps GI identity by protocol/source/link/CA/OA/QOI, observes TI100 activation/confirmation/termination and interrogated data COT20..36, counts all decoded SQ0/SQ1 objects, quality-invalid objects and unique IOAs; overlap and missing requests are labeled ambiguous/incomplete rather than successful. Master `Iec10xCommandCasebook` binds event stages to exact case evidence and prevents known cross-link feedback matches. Event Log shows GI/Command milestone rows; the EVENT header counts rows actually logged, not polling/measurement traffic. Reports expose stage references and counts. Coverage remains **Unknown** until a verified expected mapping and a complete observation window are supplied. Passive mode remains physically RX-only, and no local control action is generated by analyzers. See `docs/R3_BC_GI_COMMAND_CORRELATION_ARD_20261030.md`.

Still open in #60: explicit per-case navigation, fully verified mapping coverage, 101 multi-link physical FAT, truncated capture parity, UI long soak, and release proof. Code must not claim these complete.

## Multi-thread rule

Before coding: fetch latest master SHA, open PRs, issue #39/#19/#30, related component AGENTS and CI. Claim one requirement ID and one ownership slice per PR; reconcile shared command model or WPF changes before merge. Prefer minimal code changes, independent source parser, synthetic positive+negative regressions and exact SHA CI. This PRD states **future targets**, not current merged completion.

## R6-R9 authoritative milestones and acceptance — 2026-10-31

**Product outcome:** The application is a trustworthy, compact, high-quality IEC60870-5-101/103/104 engineering test set, protocol analyzer and **strictly receive-only IEC101 sniffer**. An operator can configure, connect/capture, observe, investigate one exact original frame, verify control/GI evidence, and export independently inspectable results. Reliability and evidence truth take precedence over effects, cards, additional feature tabs or superficial polish.

**Current audited status:** R5 master includes active IEC101/103/104, unbalanced IEC101 A/B, IEC101 passive 2/4 RX, command/GI casebooks and IEC104 STARTDT/TESTFR session hardening. User Windows observations revealed Events showing only 1/141 then 165/165 recent during ongoing traffic, even though the operator could not see an active filter. This is a **P0 release-blocking observed symptom**; a precise source cause is NOT verified. No statement of R6 completion is permitted until reproducible validation shows a stable Event Log.

### EVENT-02 [P0, R6] — Complete and stable per-object Event Journal

- Every eligible trusted **received** IEC101/104 spontaneous COT3 and appropriate return-information COT11/12 object generates a source-qualified Event fact, including first observation and legitimate repeated observations. Types include single/double points (SPS/DPS), normalized/scaled/float measurements, timed variants, and counter/integrated-total observations (BCR); preserve Type ID, COT code/P-N/TEST/OA, source/circuit, CA/IOA, value, exact original frame and object ordinal, quality and separate device/host timestamps. IEC103 protection events use native FUN/INF and relay time. Multi-object SQ0/SQ1 ASDUs must not lose the second or subsequent object. Unknown/malformed/uncertain input stays raw/uncertain, never Good.
- GI and command milestones are **transaction facts**: GI activation/ACTCON/ACTTERM, command SELECT/EXECUTE/positive/negative/deactivation/error and exact matching case/source. GI COT20..36 provides Values and GI coverage, not generic spontaneous SOE spam. Routine background/cyclic COT1/2 is not an event just because text says 'measurement'. Never infer physical circuit-breaker OPEN/CLOSE from link ACK, ACTCON or DPI alone.
- One canonical session/source/order-aware Event ledger with stable immutable fact IDs, bounded live UI projection and an **independent opt-in verified archive** for retained history. AUTO/HOLD may change follow behavior, never acquisition. In a single active session with scope All and empty query, an in-window source containing N valid events must not produce an unexplained one-row grid. Current active query/scope must be visible and count truthful, sourced from the same committed projection/version.
- Reproduce and explain the screenshot oscillation by inspecting **source count, query/scope, filtered count, WPF collection count, DataGrid bound/realized count, reset count and session epoch**, before implementing a fix. Do not mask it through constant Refresh, rebinding ItemsSource, clearing or adding independent timers.

**PASS EVENT-02:** >=5,000 and >=50,000 deterministic mixed eligible observations covering active/passive and replay variants, IEC101/103/104, SP/DP/measurement/counter, command/GI, negative/malformed, SQ0/SQ1 and redundant sources; tab changes, AUTO/HOLD and filter toggles cannot create silent data loss, stale scope or unexpected full-to-one-row oscillation. All eligible committed events resolve to exact original source frames, including repeated spontaneous and second objects. Report gaps whenever capture is incomplete, never silently claim lossless.

### UX-02 [P0, R6] — Premium compact grid-first cockpit

- Values, Events, Trace, Controls and GI use one reusable **32-DIP Search + count** row, with AUTO on chronological tabs and exactly **6 DIP** space to the first grid row. Search, count and grid must be physically aligned X/Y across tabs, not merely share the same WPF Grid.Row definition. No second title/control strip, giant cards or default bulky buttons.
- Shared outline Search/X icons, clear X only when text is nonempty, discoverable active filter/scope and stable keyboard/context menu actions. Values remains current snapshot; Events chronological evidence; Trace one physical frame per row. Copy/export preserves typed underlying values and evidence references.
- Remove large hover ToolTips on live evidence. Inspector displays **additional** link/APCI/ASDU, Type/COT, quality, byte offsets, original HEX and integrity, without repeating the same Trace/Class 2 no-data sentence. Selection is stable and does not trigger layout churn.
- Calm premium industrial blue-grey theme: central tokens with slightly stronger text, border, selected-row and grid header contrast; meaningful colored status badges with text, compact normal/medium typography, no dense animations or heavy bold. Accessible at genuine Windows 100/125/150% DPI.

**PASS UX-02:** Repeated real WPF screenshots and geometry assertions: <=2 DIP target Search and grid alignment, 6-DIP gap, no overlay tooltip or scrolling surprise, functional source navigation, correct copy and no clipped primary controls. CI screenshot requested at 1366 does **not** prove physical 1366 if runner constrains its dimensions.

### PERF-01 [P0, R6] — Bounded memory and loss-observable high throughput

- Separate COM/TCP/strict passive acquisition, parser/state and journal from WPF. Use one ordered canonical writer and **epoch/version-consistent incremental UI projection**; no per-frame Dispatcher calls, duplicate parser or full list re-filter/Reset per ordinary 75ms refresh. Preserve stable row IDs, scroll/selection and HOLD. Zero CollectionChanged.Reset during unchanged-scope live batches.
- Render coalescing is permitted; **loss of eligible authoritative Event facts is not silently permitted**. Bounded raw/event queues need safe backpressure/spooling and recorded fault/incomplete/loss counters; DropOldest/DropNewest/DropWrite are for explicitly disposable UI previews only. Raw full-history claims only apply when capture was enabled and integrity/health verified. Bounded in-memory Event row limit is NOT full archival history.
- Instrument capture/source/version, queue depth/peak, filtered/bound grid counts, event loss, collection resets, latency, CPU/GC/memory and archive throughput without private payload in logs. Paging/indexed disk history belongs to R7.

**PASS PERF-01:** 50k event stress with bounded WPF collection; no data misclassification, 0 ordinary Reset, consistent committed counts; provisional targets <=150ms p95 event-to-visible latency and <16ms p95 Dispatcher batch on a **declared reference PC/workload**. Report actual measurements, not target numbers presented as achievements.

### TRACE-02 / CAPTURE-02 [P0/P1, R7] — Full source-byte forensic parity

Finish byte-accurate IEC103/104 inspectors using the authoritative decoders, correct SQ implied IOA spans, exact event-to-frame evidence, corrupted/legacy file handling, complete live/replay/JSON/PDF parity and indexed/paged read-only archives with cancellation and bounded memory. **PASS:** mixed positive/negative fixture comparisons from byte offsets through report, malformed and unverified archives explicitly marked, no unbounded memory or per-keystroke full-history read.

### TASK-01 [P1, R8] — User-configurable Master/RTU Test Set

Add tested IEC101/104 RTU/slave simulation with editable point database, task/exchange scenarios, GI/control/clock behavior, and **lab-only** error injection. Active test commands require clear authorization, target, safe preview and source-qualified proof. Existing IEC103 deterministic simulator is a test fixture, not full RTU simulation. **PASS:** cross-implementation tests and negative protocol behavior; self-testing simulator alone does not establish interoperability.

### FAT-02 [P0/P2, R9] — Professional release/bench proof

Test at least independently implemented protocol peers plus authorized IEC101 2/4-RX physical electrical isolation, 1200-bps long soak, IEC103 relay, IEC104 TCP timing t1/t2/t3/k/w and reconnect, command/GI state, all flag/measurement extrema, archives, genuine 100/125/150% DPI and CPU/memory/latency. Every advertised feature must declare **Implemented / Automated Tested / Physically Tested / Partial / Not Implemented**. Balanced IEC101 active mode and NUC certification remain separate gates.

### Mandatory milestone sequence and stop/go

| Order | Meaningful delivered checkpoint | Stop/go |
|---|---|---|
| **R6 FIRST** | EVENT-02 + UX-02 + PERF-01 in integrated Windows x64 build | Do not close until full-to-one Event flicker is reproduced, root cause fixed, mixed-event semantics verified, GUI screenshots audited and exact-head CI passes |
| **R7** | TRACE-02 + CAPTURE-02 deep forensic proof | Verified original-byte reference from all objects into archive, replay and reports |
| **R8** | TASK-01 active task and configurable 101/104 RTU simulator | Verified against independent implementations and safety/negative test matrix |
| **R9** | FAT-02 production acceptance | Real electrical, serial/TCP, long-soak, DPI/CPU proof and truthful mode/capability matrix |

**CI/resource discipline:** cheap unit/integration checks first, one large meaningful PR at a time, one full Windows Release/format/protocol smoke/xUnit/real-WPF/CodeQL/dependency/hygiene gate at exact head, verified portable Windows ZIP+SHA256. No merge based only on source syntax or one screenshot. No private customer traces, no competitor-specific code or proprietary GUI copying. Protocol and research constraints in the original 2026-10-29 PRD remain mandatory.

**External basis (independent concepts only):** https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls ; https://learn.microsoft.com/en-us/dotnet/core/extensions/channels ; [controlled IEC/NUC research](IEC10X_NUC_RESEARCH_20261029.md). This contract is a **target**, NOT an assertion that R6-R9 are implemented.
