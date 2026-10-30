# PRD — ARIEC60870 Professional IEC-101/103/104 Interoperability & Forensics

**Date:** 2026-10-29. **Status:** canonical **product-direction** requirements for all implementation threads, not a statement that all features are complete. Read [research](IEC10X_NUC_RESEARCH_20261029.md) and [ARD](IEC10X_INTEROPERABILITY_ARD_20261029.md). Verify live master, open PRs, checks and issue #39/#19/#30 before work; historical component PRDs must not override merged code.

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

## Delivery order and acceptance

**R0 document alignment:** this PRD/ARD/research + AGENTS; documentation only, no interoperability implementation claim. **R1 P0 safety:** reconcile current command-verdict and raw evidence integrity work, correct source-aware flags/measurements/replay with tests. **R2 profiles/GUI:** versioned generic vs NUC vs project profile and byte inspector parity 101/103/104. **R3 field/quality:** real multi-vendor FAT, reports parity and measured UI/CPU. **R4 release candidate:** reproducible tested Windows ZIP, hash/SBOM, truthful capability matrix.

No NUC certification, balanced active TX, production SCADA replacement, private capture upload, or competitor UI copying without separately approved scope.

## R3-B/C 2026-10-30 implementation checkpoint — EVENT-01 / CMD-01 / CAPTURE-01

R3-A (#59) is merged. R3-B/C continues with source-qualified GI conversation evidence, not a second WPF or parser engine. The Master-layer `Iec10xGiCasebook` keeps GI identity by protocol/source/link/CA/OA/QOI, observes TI100 activation/confirmation/termination and interrogated data COT20..36, counts all decoded SQ0/SQ1 objects, quality-invalid objects and unique IOAs; overlap and missing requests are labeled ambiguous/incomplete rather than successful. Master `Iec10xCommandCasebook` binds event stages to exact case evidence and prevents known cross-link feedback matches. Event Log shows GI/Command milestone rows; the EVENT header counts rows actually logged, not polling/measurement traffic. Reports expose stage references and counts. Coverage remains **Unknown** until a verified expected mapping and a complete observation window are supplied. Passive mode remains physically RX-only, and no local control action is generated by analyzers. See `docs/R3_BC_GI_COMMAND_CORRELATION_ARD_20261030.md`.

Still open in #60: explicit per-case navigation, fully verified mapping coverage, 101 multi-link physical FAT, truncated capture parity, UI long soak, and release proof. Code must not claim these complete.

## Multi-thread rule

Before coding: fetch latest master SHA, open PRs, issue #39/#19/#30, related component AGENTS and CI. Claim one requirement ID and one ownership slice per PR; reconcile shared command model or WPF changes before merge. Prefer minimal code changes, independent source parser, synthetic positive+negative regressions and exact SHA CI. This PRD states **future targets**, not current merged completion.
