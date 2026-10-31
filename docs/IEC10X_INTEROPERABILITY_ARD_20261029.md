# ARD — ARIEC60870 Interoperability & Evidence Architecture

**Date:** 2026-10-29. **Status:** canonical design/ADR for all threads, **not** a completed implementation claim. Companions: [PRD](IEC10X_INTEROPERABILITY_PRD_20261029.md), [research basis](IEC10X_NUC_RESEARCH_20261029.md). Respect existing merged Passive P0–P4A, dual-link, command and Quality work. Evolve incrementally, not rewrite.


> **ACTIVE R6-R9 ARCHITECTURE AUTHORITY — revision 2026-10-31.** [Section 9](#9-r6-r9-engineering-architecture--2026-10-31) supersedes historical R0-R4 scheduling where conflicting; existing protocol/capture ADR-01/02/03 and R5 active Master safety remain binding. Read [PRD](IEC10X_INTEROPERABILITY_PRD_20261029.md) and [AGENTS](../AGENTS.md). Event Log root cause and R6 implementation are **not yet validated** by this document.

## 1. Decisions and component ownership

**ADR-01:** One parser for each wire semantic; one authoritative raw/typed evidence ledger; independent transport state machines; thin WPF projections. The 101/104 common ASDU decoder must not duplicate decode in WPF. IEC103 FUN/INF remains independently interpreted. Master owns active serial/TCP, FCB, polling, redundancy decisions. Monitor owns RX-only, archive and passive correlation and **must not contain active TX**. Desktop only coordinates settings, commands, rendering and export; report/replay consume typed evidence, not visible row strings.

**ADR-02:** Protocol profile = selected, versioned configuration with generic IEC / optional historical NUC / explicit project overrides. A selected NUC101 profile does not imply "all IEC101" and cannot silently force CA/COT/IOA lengths or GI-after-switch. Future balanced modes must be separately designed/tested before enabled TX.

**ADR-03:** Distinguish evidence grade ObservedValid / ObservedUnverified / Correlated / Inferred / NotObserved; no module upgrades uncertain raw to successful proof by string parsing.

## 2. Canonical conceptual evidence schema (add adapters, do not break old .ariec)

~~~text
CaptureSession: sessionId, archiveFormatVersion, selectedProfileId+version, mode, physical topology, UART/TCP metadata, timestampAccuracy, integrityState
PhysicalSource: sessionId, circuit A/B/single, transmitter direction, streamId, adapter/endpoint
FrameFact: frameId, sourceId, original stream offset/length, raw ledger ref, checksum/parseResult, host timestamp, linked ASDU ids, linked raw byte spans
AsduFact: asduId, parent frameId, Type, VSQ/SQ, COT numeric/P-N/TEST/OA, CA, actual field widths, object ids, validity
ObjectFact: objectId, parent asduId, ordinal, IOA+optional physical span, raw value, signed/normalized/float codec, QDS/SIQ/DIQ/BCR, CP timestamp, exact byte spans
Exchange: requestFrameId, responseFrameId?, retry ids, correlationEvidence, ambiguity, latency uncertainty
Transaction: type (GI/command), source-scoped stage ids, valid/unknown/negative stages, expected feedback, observed outcome and uncertainty
~~~

A session-qualified ID is never an IOA, WPF row index or mere address. For SQ=1 only first IOA is physically carried; later implicit IOAs **have no byte span**. Retain original bytes, checksum, width/profile and source identity. Legacy captures without width metadata show original raw+summary and Unknown typed fields, not guessed good data.

## 3. Raw acquisition and safe parsing

Capture RX before filtering, UI or decode; archive all 10/68/E5, unexpected A2/noise/truncated/invalid bytes, source offsets and receive errors. Per-source FT1.2 scanner with bounded forward progress. Validate frame start/length/checksum/end; *only then* promote ASDU to trusted typed interpretation. 101 COT 1/2, CA 1/2, IOA 1/2/3, link 0/1/2 as lawful; 104 APCI I/S/U plus 10x ASDU; 103 FUN/INF separately. Emit typed Result with field spans, not exception for normal malformed input. Store all objects including SQ1 implied addressing.

Preserve time-of-device vs time-of-host and invalid bit; never substitute host arrival as device time. Two/four USB COM reception callbacks do **not** prove on-wire ordering, electrical isolation, RTS/CTS continuity or 33-bit idle. Historical re-decode with optional changed profile is read-only and separately attributed, never changes hash/source capture.

## 4. Protocol policies and distinct state machines

### IEC101 active unbalanced

Independent per-link states: Closed, StatusProbe, StatusObserved, ResetSent, ResetAck, ActivePolling, StandbyStatus, Suspect, Recovering, Demoted. Confirm per-link FC/PRM/FCB/FCV/ACD/DFC and timeout. Primary FC0 reset, FC9 status, FC10/11 class request; secondary FC0 ACK, FC8 user data, FC9 no requested data, FC11 status. Single E5 **contextual**; no unearned response match; unexpected A2 stays unsupported raw. Never treat FC9 no-data as negative application confirmation.

NUC101 §7.2.1: on startup check both A/B; reset and poll selected active; standby status probe only; failover resets and polls promoted channel; preserve FCB and event acknowledgment continuity. No cross-circuit E5/response pairing. GI after unbalanced line switch is **optional**. Config enum: Disabled, Manual, OnFailover, ProjectRequired; preserve current product default until reviewed migration. Missing GI is a finding **only when project policy requires it**, not universal NUC law.

### IEC101 balanced

Different bidirectional initiating roles/FCB and NUC101 §7.2.2 link test ACK on active/NACK on passive, last application ASDU role behavior. **Do not reuse unbalanced polling or Link Status semantics.** Not active-TX enabled until new isolated engine, golden fixtures and approved hardware FAT.

### IEC104

TCP endpoint/session identity, APCI I/S/U, N(S)/N(R), STARTDT/STOPDT/TESTFR, retransmission/ack supervision and reconnect. NUC104 frontend takeover with GI is an **individual deployment profile**, not an unbalanced 101 default. New TCP session never silently inherits a previous command outcome.

### IEC103

Separate FUN/INF/application control decoding, preserving relay-specific quality/time and unmapped/unknown vendor payload. Share evidence contract and UI intent, not IEC101 wire-field assumptions.

## 5. Derived facts & safety engines

**Events/GI:** Each valid spontaneous or return-information received object is preserved with physical source. Values identity includes protocol/source/CA/IOA; logical GI completion uses user-approved CA+IOA mapping without counting cross-CA or duplicate A/B observations as extra points. GI Type100 stages COT6/7/10 and intermediate data have source-qualified exact evidence. NUC event-driven application traffic and interleaved event/GI are expected. A configured queue-overflow point is separate from QDS OV; no implicit IOA=1 mapping without approved profile.

**Commands:** Identity includes capture session, protocol, physical link, CA, IOA, Type and S/E plus request identity. Lifecycle Select→ACTCON→Execute→ACTCON→ACTTERM, optional break-off COT8/9, negative P/N and COT44–47; source/phase mismatch and overlapping pending requests are **ambiguous**, never accepted by last-wins. NUC101 explicitly allows process feedback and ACTTERM in either arrival order. Absence of ACTCON on a partial capture means observed-incomplete, not remote definite failure. Good feedback requires actual expected target state, good typed quality and source, not link ACK or any matching IOA. ACTCON / ACTTERM does not prove physical switchgear operation.

**Measurements:** Type9 normalized, Type11 signed INT16 little-endian, Type13 IEEE float have separate codecs, including CP time-tag variants. Retain value raw bytes, normalized/scaled mapped engineering unit, sender QDS and transformation metadata independently. Test 0x7FF3=32755, 0x7FFB=32763, 0x7FFF=32767, 0x8000=-32768, 0x8001=-32767 with OV=0 and 1 separately. Sign boundary is not a proven gateway fault. Different layers: QDS OV, BCR carry, link DFC, mapped buffer-overflow indication, UART overrun.

## 6. WPF and reporting contract

One physical frame row and nested ASDU/object drilldown; direct arrow/physical A/B, timestamp quality, link/FC, Type, COT numeric with NEG/TEST chips, CA/IOA, Quality max-three chips, raw hex; copy exact verified byte slice. 103/104 Inspector eventually byte-true like 101. No extra per-flag columns, noisy hover, giant cards or per-byte Dispatcher work. Keep 75ms batching/1200-row bounded trace as current implementation baseline **subject to measured optimization**, not universal design law.

Events, Values, command stages, findings and PDF read same typed IDs; report states capture integrity, profile, source and observed/inferred distinction. GUI queue may drop previews but disk raw evidence never silently drops. Handle hour-scale capture by index/pagination not loading every record into RAM. Startup/Stop and 100/125/150% DPI require real measurement.

## 7. Release and FAT gates

| Gate | Required evidence |
|---|---|
| V01 frames | FT1.2 E5/A2/fixed/variable, 0/1/2 legal link widths, malformed length/checksum/noise and per-source raw offsets. |
| V02 ASDU | Type, SQ0/SQ1, COT 1/2 P-N TEST OA, CA1/2, IOA1/2/3; exact per-object byte map and invalid/unknown preservation. |
| V03 quality/measurement | SIQ/DIQ/QDS/BCR, Type31 DIQ no overflow, signed INT16 boundaries and sender OV independent; queue indication/DFC separate. |
| V04 NUC101 unbalanced | A/B startup/standby/switch/FCB/retry/event retention; GI-optional vs GI-project-required; no false status-only fault. |
| V05 balanced | Independent bidirectional NUC link test and response fixtures; mode stays unsupported until active-TX implementation and hardware acceptance. |
| V06 commands | SBO/direct/select/execute/deactivate/negative/Type44–47, feedback before or after ACTTERM, corrupted/ambiguous/unobserved response. |
| V07 capture parity | Same frame/object/transaction IDs and values in live/replay/JSON/PDF with hashes; bad ZIP and legacy widths degrade honestly. |
| V08 other protocols | IEC104 APCI and reconnect, IEC103 FUN/INF/CP time, supported hardware behavior and Inspector span parity. |
| V09 field/UI | Isolated 2/4 RX, 1200bps/other rates, long soak, physical timestamp instrumentation where required, Windows 100/125/150% DPI and CPU/allocations/Stop measurements. |

CI Release build, protocol smoke, xUnit, GUI startup, CodeQL, hygiene and verified Windows package on exact head are required; **CI does not prove field interoperability**. Prefer small cheap fast tests, one packaging gate per meaningful deliverable, no wasteful loops.

## 8. Historical R0-R4 concurrent-thread ownership and roadmap (superseded in priority by R6-R9)

**R0 (this documentation PR):** canonical research/PRD/ARD, AGENTS pointers, no runtime changes. **R1:** reconcile active command-verdict PRs; add tests for overflow/numeric/GI-policy and full replay parity. **R2:** optional versioned project/NUC profile rules and Inspector 103/104. **R3:** measured WPF and durable report consistency. **R4:** independent multi-vendor field matrix + release candidate. Balanced active IEC101 is separate gated scope.

Every thread MUST refresh master/open PRs/issues before working; choose one requirement ID, one owned module, one branch/PR; test positive and negative fixtures, verify latest CI SHA, and reconcile concurrent shared WPF/command edits instead of overwriting. Old component design docs are historical; committed code plus exact CI is implementation truth, this ARD is target behavior.

## R3-B/C ownership and evidence contracts — 2026-10-30

This checkpoint adds one **Master-owned GI transaction state** to the established Master `Iec10xCommandCasebook` and existing Monitor PassiveGiTracker (no duplicate raw parser). `Iec10xGiCasebook` is bounded per source/session with exact Protocol, physical source and link, CA, originator and QOI identity. Valid Type100 COT6/7/10 lifecycle, COT20..36 process objects, SQ1 implied IOAs, quality and source refs are accumulated. Overlap/missing stages remain Ambiguous/Observed incomplete; ACTTERM means observed termination only. No modeled coverage percentage without user-verified mapping.

Desktop only projects these immutable GI milestones to Events and summary to export. Counter EVENT increments on actual Event Log additions, not on all measurements. Command-case diagnostics and event labels use an exact evidence reference rather than last-created case. Incorrect or unknown physical feedback cannot be upgraded to proven breaker motion. No passive TX enabled.

Next milestones: full Event→Controls jump by source qualified IDs; verified mapping coverage; replay/export equivalence; 1200bps and 2/4 RX bench FAT; CI at exact HEAD. See `R3_BC_GI_COMMAND_CORRELATION_ARD_20261030.md`.

## R2-A verified profile / active-TX gate implementation (2026-10-30)

A **real, versioned profile value object** now lives in `Master/Model/Iec10xInteroperabilityProfile.cs`. It records protocol, selected optional edition (generic IEC versus historical NUC101/104), explicit field widths, IEC-101 mode, serial GI-after-switch policy, and fingerprint. Its strict JSON `Import`/`Export` uses schema version 1; unknown fields, unsupported versions, malformed profiles and unsupported widths are rejected. Import/export is an engineering API at this milestone, **not yet an arbitrary-file GUI import function**.

The WPF Setup has an explicit convention selector defaulting to **Generic IEC**, persisted in setup preferences. Historical convention is opt-in and does **not** silently override COT/CA/IOA/address widths or require GI after IEC-101 line switch. No historical convention may claim IEC-103 coverage. `BuildSettingsFromUi` uses `ValidateActive` **before opening an active transport**. IEC-104 enforces fixed 2/2/3 ASDU widths, IEC-103 supported 1/1/1 dimensions, and IEC-101 refuses unimplemented balanced master TX and invalid source/address ranges. On failure the operator sees a concrete pre-connect error. Settings/report snapshots carry edition and fingerprint without changing the raw archive.

**Non-claims:** historical NUC selection is a reference/interpretation label, not interoperability certification; balancing, auto GI after switch and custom edition file picker remain separately gated future work. Passive monitoring remains receive-only, and importing/reinterpreting already captured evidence is never blocked merely because active TX is unsupported. Test scope: round-trip and malformed JSON, unsupported edition, wrong protocol/field width/addresses, bounded profile identity, and serial/TCP protocol safety.

## R5-A — IEC-104 STARTDT fail-closed / modular sequence safety (2026-10-31)

The active Master now gates ALL I-format TX behind a positive, valid STARTDT confirmation; an unexpected U-format reply, malformed APDU or missing confirmation yields a specific source-backed finding and a stopped faulted session, not blind GI/clock-sync/command transmission. TESTFR supervision faults rather than silently pretending a healthy line when no confirmation arrives. The Master recognizes valid interleaved frames while awaiting U-format confirmation (bounded search); original RX bytes remain in forensic evidence.

APCI N(S)/N(R) wrap modulo 32768. A peer's cumulative N(R) can advance acknowledgements only inside the bounded outstanding k-window; duplicates/future N(R) never silently pass. TX blocks on a full configured k-window. Invalid/out-of-sequence I-ASDUs remain captured as RX Warning but cannot silently update process state or event/point series, and their sequence cannot advance the contiguous receive window. Handshake/sequencing tests include negative STARTDT and rollover/future ACK. **This is a safety hardening milestone, not a claim that IEC-104 t1/t2/k/w/reconnect interoperability has completed multi-vendor FAT.**

## 9. R6-R9 engineering architecture — 2026-10-31

### ADR-04: Diagnose source -> filter -> WPF projection before modifying it

A user's Windows run displayed Event Log 1 / 141 recent and subsequently 165 / 165 recent with no apparent active search filter. **The symptom is real, not a verified diagnosis** of a thread race, missing events, or virtualization failure.

Inspect the current flow: MainWindow.MappingValueStatus.cs ApplyRelayEventFilter reads the bounded event store, filters using Events search and a **collapsed scope ComboBox**, and may call RelayEventRows.ReplaceRange (Reset) for nonoverlapping snapshots. LiveEvidencePipeline flushes dirty Events per UI batch; WorkspaceSelection also queues refresh on return to Events. ObservableRangeCollection emits precise Add/Remove on normal overlap but Reset on ReplaceRange. Capture source, filtered count, projection count, bound DataGrid count and actually realized row count separately before changes.

Instrument one sanitized record per committed UI version: SessionEpoch; CanonicalThroughSequence; CanonicalCount; WindowCount; FilterScope; QueryActive; FilteredCount; ProjectionCount; GridItemsCount; VisibleRealizedCount; ResetCount; QueueDepth; BatchMs; CaptureHealth. Do not log customer bytes, signal names, values, IPs or serial settings by default. Detect the four distinct cases: (1) intentional filtered 1/141, clearly labeled; (2) source/window scope change; (3) projection mismatch; (4) WPF realization failure. **No blind Refresh/InvalidateMeasure, ItemsSource detach/attach, extra timers or full collection replacement as a band-aid.**

### ADR-05: One original-source, immutable per-object event authority

Logical pipeline, implemented by adapting existing modules, **not by rewriting or duplicating the parser**:

    Active COM/TCP Master | passive IEC101 strict RX-only | verified offline replay
          -> existing ordered raw/frame evidence with physical source, session, offsets, receive time
          -> canonical IEC101/104 ASDU decoder; independent IEC103 protection decoder
          -> typed FrameFact / AsduFact / ObjectFact for ALL decoded VSQ/SQ objects
          -> protocol-owned classification / GI + command correlation
          -> one ordered canonical EventFact authority + independent current Values snapshot
          -> epoch/versioned EventProjection -> bounded batched WPF grid
          -> verified on-disk raw/event archive and indexed replay, ONLY where enabled

Recommended immutable EventFact fields: SessionId, Protocol, PhysicalSource/Circuit, StreamId, PhysicalFrameIdentity, ObjectOrdinal, EventKind, ReceiveSequence, TypeId, COT numeric/P-N/TEST/OA, CA/IOA or IEC103 FUN/INF, valid raw byte spans, OriginalEvidenceReference, typed value and raw representation, SIQ/DIQ/QDS/BCR (when supported), DeviceTimestamp+InvalidBit, HostArrival+Accuracy, validity/trust grade and correlation/uncertainty. For SQ=1, implied successive IOAs must NOT invent non-existent IOA byte spans. The identity is **session/source/frame/object/kind**, never display text, IOA alone, or WPF row index.

Classification contract:
- Valid received IEC101/104 COT3 spontaneous SP/DP, timed variants, normalized/scaled/float measurement and counter/integrated-total objects, plus profile-relevant COT11/12 return info are per-object Event facts **even when first received or repeated**. Correct quality/raw flags preserved; IEC103 protection uses native FUN/INF and device time, not IEC10x fake identity.
- GI TI100 activation/ACTCON/ACTTERM and control select/execute/confirmation/rejection/deactivation/error must use source-qualified transaction milestone kinds, with exact original frame evidence. GI COT20..36 data updates Values and GI casebook; no automatic per-object SOE from snapshots. COT1/2 cyclic/background generally updates Values; an independently verified change can be described accurately without changing its COT to spontaneous.
- Unknown/malformed frames are raw observations, not typed good process events; out-of-order or invalid IEC104 I frames do not elevate into Values. Preserve R5 STARTDT gating and k-window safeguards. Avoid claiming physical command success from ACTCON, a link ACK or mere DP status. Preserve valid first/repeat event distinctions and A/B+CA source isolation.
- Chronology per physical stream/receive sequence, with host timestamp uncertainty across USB COM adapters explicitly stated. Relay/device timestamp is never silently replaced by PC arrival; original frame link and raw evidence remain immutable.

### ADR-06: Memory, backpressure and archive health are different contracts

**Capture/authority path** and **GUI preview path** are distinct. RX acquisition does not call Dispatcher and must never depend on Trace/Event HOLD or virtualization. Keep current transport workers and verified passive append-only journal. Use bounded source queues, one writer per relevant ordered domain, batch ingestion, cancellation, and measured memory limits.

- Mandatory frame/event facts cannot disappear silently under a queue overflow. For .NET bounded Channels, Wait exerts backpressure; DropOldest/DropNewest/DropWrite deliberately discard. Use bounded staging/spooling and monitored writer throughput; if correctness cannot be preserved, signal Incomplete/Overflow with affected source and sequence gap, and do not claim lossless. Do not block a hardware receive callback while holding WPF or protocol locks.
- The UI may coalesce *rendering* or discard explicitly designated low-value preview traffic with observable counters, but never reclassify those drops as a complete recording. A bounded 420-row Events view / 1200-row Trace view is a viewport, not all historical bytes. Verified durable archive and capture mode must be opt-in, health-checked, and replayed from original bytes with CRC/SHA and source offsets.
- Index large archives by block/time/source/object reference; provide bounded pages, generation-guarded cancellation, and corruption/truncation/legacy uncertainty. Never scan entire multiday archive on every keystroke or load full history into memory.

### ADR-07: Epoch/versioned incremental EventProjection with truthful counts

Recommended conceptual UI snapshot:

    EventSnapshot(
      SessionEpoch, ThroughSequence, FilterVersion, FilterScope, QueryActive,
      CanonicalCount, RetainedWindowCount, FilteredCount, VisibleCount,
      StableEventRows)

    EventDelta(Epoch, BaseVersion, NewVersion,
      AddedRows, ExpiredRows, ChangedRows)

An ordered writer publishes immutable facts; WPF Dispatcher applies one coalesced, epoch/version-checked delta to a bounded collection. On ordinary live appends use precise Add/Remove/Replace with stable ID, not ReplaceRange/Reset. A filter or capture change is an explicit new scope/version; discard stale deferred operations without clearing a newer session. Separate persistent authoritative data from bounded DataGrid projection.

The displayed numerator and denominator come from **one committed projection version** (for example 140 / 141 recent, filtered by Analog). Do NOT mix RelayEventGrid.Items.Count with a differently timed source snapshot. Filter is discoverable; if All+empty and N in-window source rows exist, an unexplained single projected row is a P0 fault. AUTO/HOLD only follows tail or preserves scroll anchor and selection. User search never changes raw capture nor another tab's canonical fact store.

Retain WPF recycling and virtualization; WPF UI virtualization is not data virtualization. Existing 75ms flush is a current baseline and must be benchmarked, not blindly shortened. Use no per-frame Dispatcher calls, per-row effects, additional timer loops or UI re-decoding.

### ADR-08: Premium shared interface and nonrepetitive explanations

- One shared compact 32-DIP Search | count | AUTO (AUTO where chronological) on Trace, Values, Events, Controls and GI; all grid top edges 6 DIP below the toolbar, search left edges aligned with each grid. Do not perpetuate existing Values 8 DIP vs Events 6 DIP difference. One common outline Search/X style, X only for a nonempty query, active filter explicitly visible.
- Data grid remains the main content; contextual selection and right-click enable original frame navigation, byte-true copy and filter. Eliminate intrusive ToolTips on live evidence rows, especially popup ProtocolTraceMeaning. Inspector explains **additional** bytes, link FC/flags, APCI/ASDU, COT, object count/offset, quality/uncertainty, raw HEX and provenance, not repeated Class2 no-data or Trace header text.
- Use centralized calm blue-grey palette with just enough contrast for headers, selected rows, borders and secondary text; restrained semantic ON/OFF/warning/unknown chips with words, not color alone. No big cards, heavy bold, layout jitter or animated live rows. The original byte evidence is selected/copyable without noisy hover.
- Windows WPF runtime acceptance checks actual Control.TranslatePoint coordinates, toolbar height, gap, selected event row realization, multi-tab reentry and accessibility. CI window constraints and simulated 150%-DIP requests must be identified as nonphysical; actual 100/125/150% Windows DPI is R9 field proof.

### ADR-09: Evidence-driven milestones and hard quality gates

| Milestone | Engineering checkpoint | Acceptance / failure criterion |
|---|---|---|
| **R6 [P0]** | EVENT-02 + UX-02 + PERF-01 in one coherent Windows build | Reproduce/diagnose full-to-one-row oscillation; 5k/50k mixed-event tests including malformed, SQ0/SQ1, repeats, A/B; 0 unchanged-scope Reset, source/filter/projection/grid version parity; true original refs; stable AUTO/HOLD and selected row; real-WPF visual inspection |
| **R7 [P0/P1]** | TRACE-02 + CAPTURE-02 | Exact 101/103/104 field offset proof, replay/live/JSON/PDF parity, source-indexed long capture, corruption/unknown integrity and bounded memory |
| **R8 [P1]** | TASK-01 IEC101/104 operator RTU simulator and test tasks | Independent peer verification, controlled command/negative behavior, simulator and Master not only testing each other |
| **R9 [FAT]** | Field electrical/serial/TCP/DPI proof | 2/4 RX electrical safety + 1200-bps long soak, IEC103 relay, IEC104 multi-vendor t1/t2/t3/k/w/reconnect, actual 100/125/150% DPI and CPU/GC/latency metrics |

Performance **targets pending measurement**: <=150ms p95 event-to-visible latency, <16ms p95 Dispatcher batch on a declared reference PC/workload, bounded WPF memory under 50k source facts, no drop of eligible authoritative events while recording is enabled and healthy. If benchmark is worse, document numbers and repair bottleneck rather than claim parity.

Implementation gates: first inspect master, open PRs, current CI, component AGENTS and exact requirement ID. For P0, obtain a failing regression/telemetry before modifying runtime. Test positive and negative behavior and keep protocol truth in Master/Core/Monitor. Cheap tests before consolidated Windows CI; one meaningful PR per milestone/slice, exact-head Release build/format/protocol smoke/xUnit/WPF screenshot acceptance/CodeQL/dependency/hygiene plus self-contained x64 ZIP/SHA256. Physical FAT is **never** inferred from synthetic CI. Never publish private traces or competitor proprietary code/GUI.

### Independent engineering references

- WPF container recycling vs. data virtualization: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-controls
- Bounded Channel backpressure and loss modes: https://learn.microsoft.com/en-us/dotnet/core/extensions/channels
- IEC editions and optional NUC restrictions: [controlled research](IEC10X_NUC_RESEARCH_20261029.md)

**Status:** This section sets future architecture/delivery authority. It does NOT say R6-R9 code, multi-vendor interoperability or physical FAT is complete.
