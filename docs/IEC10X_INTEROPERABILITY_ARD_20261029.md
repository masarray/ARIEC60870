# ARD — ARIEC60870 Interoperability & Evidence Architecture

**Date:** 2026-10-29. **Status:** canonical design/ADR for all threads, **not** a completed implementation claim. Companions: [PRD](IEC10X_INTEROPERABILITY_PRD_20261029.md), [research basis](IEC10X_NUC_RESEARCH_20261029.md). Respect existing merged Passive P0–P4A, dual-link, command and Quality work. Evolve incrementally, not rewrite.

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

## 8. Concurrent-thread ownership and incremental roadmap

**R0 (this documentation PR):** canonical research/PRD/ARD, AGENTS pointers, no runtime changes. **R1:** reconcile active command-verdict PRs; add tests for overflow/numeric/GI-policy and full replay parity. **R2:** optional versioned project/NUC profile rules and Inspector 103/104. **R3:** measured WPF and durable report consistency. **R4:** independent multi-vendor field matrix + release candidate. Balanced active IEC101 is separate gated scope.

Every thread MUST refresh master/open PRs/issues before working; choose one requirement ID, one owned module, one branch/PR; test positive and negative fixtures, verify latest CI SHA, and reconcile concurrent shared WPF/command edits instead of overwriting. Old component design docs are historical; committed code plus exact CI is implementation truth, this ARD is target behavior.
