# ARD — IEC-101 Passive Serial Sniffer (2/4 COM), Loss-Aware Forensic Pipeline

Status: Approved-for-implementation proposal; **design only, no code yet**  
Date: 2026-10-27  
Baseline: master 52777a7858d28b53c0739112219ac7685008ec84  
Companions: IEC101_PASSIVE_MONITOR_PRD.md; IEC101_PASSIVE_MONITOR_UX.md; issue #19.

## 0. Executive architecture decision

Create a new, separate **ARIEC60870.Monitor** .NET 8 runtime assembly for RX-only serial acquisition, archival, streaming FT1.2 extraction, passive observation, and analysis. The WPF shell selects this runtime when “IEC-101 Passive Monitor” is chosen. Reuse protocol parsers but never instantiate active master sessions. WPF receives bounded summaries and must never be the evidence repository.

Invariants: no serial write capability in passive runtime; two monitoring UART RX inputs per *one* full-duplex RS-232 line; four inputs needed to observe both directions on two physically redundant lines; complete raw capture and evidence quality distinct from operator display.

## 1. Code-level reality audit (do not overlook)

| Existing file | Reality | Planned action |
|---|---|---|
| Core/Parsing/Ft12Parser.cs | Parses 10/68/E5/A2 FT1.2 framing/link but its embedded AsduDecoder is **IEC-103**, not IEC-101 | Reuse pure frame/link logic with a narrow new framing-only option or extraction; do NOT trust Ft12FrameDecode.Asdu for IEC-101 |
| Core/Parsing/AsduDecoder.cs | IEC-103 header FUN/INF assumption | Never use for IEC-101 ASDU |
| Master/Protocol/Iec10x/Iec10xAsduDecoder.cs | Actual IEC-101/104 Type/COT/CA/IOA/value/quality decoder, configurable widths | Move/introduce shared *decode-only* code to Core in a separately tested migration; preserve Master compatibility via forwarding shim or explicit shared projection. No copied divergent decoder |
| Master/Protocol/Ft12StreamReader.cs | Active request/response framed reads; ignores stray bytes on start search, timeout returns fragments | Do NOT use for raw acquisition or passive reassembly |
| Master/Transport/SerialByteTransport.cs | TX/write operations, active modem outputs, stream response timeout, discard input buffer | Do NOT use in Monitor mode |
| Master/Iec101/Redundancy/Iec101DualLinkRedundancySession.cs | ARIEC-controlled active master failover | Do NOT use as ETERA switch detector |
| Desktop/MainWindow.LiveEvidencePipeline.cs | WPF 75ms dispatcher flush, bounded store; Balanced suppresses routine trace if tab hidden | Reuse rendering principles; NEVER source forensic recording from this pipeline |
| Desktop/MainWindow.CaptureFiles.cs | ARIEC_CAPTURE_V1 ZIP written from selected/visible EvidenceRows | Keep V1 reader, introduce separate V2 writer/reader; do not mutate V1 semantics |
| Desktop/ViewModels/BoundedRingBuffer.cs | Bounded UI projection | Reuse in presentation only |

Risk P0: app branding “Trace” suggests comprehensive data, but exported visible/selected trace may miss routine frames. Monitor's capture integrity must not depend on the view or its filters.

## 2. Dependency rule

Target references:

    ARIEC60870.Desktop -> ARIEC60870.Monitor -> ARIEC60870.Core
    ARIEC60870.Master  -> ARIEC60870.Core
    ARIEC60870.Cli     -> Monitor/Core (for offline replay later)

ARIEC60870.Monitor MUST NOT reference Master or Desktop. Extract only pure IEC-10x decode models, decoder and narrowly needed lookup methods to Core. First inventory transitive dependencies and migration diff; maintain namespaces via one compatibility adapter if necessary; preserve existing 101/104 test fixtures byte-for-byte. Avoid mass refactors.

Suggested file map (names are proposals, not existing code):

    src/ARIEC60870.Monitor/
      ARIEC60870.Monitor.csproj
      Abstractions/IReadOnlySerialTap.cs
      Models/PassiveMonitorOptions.cs
      Models/SerialStreamId.cs
      Models/CaptureRecord.cs
      Session/PassiveMonitorSession.cs
      Transport/WindowsReadOnlySerialTap.cs
      Capture/RawCaptureJournalWriter.cs
      Capture/RawCaptureJournalReader.cs
      Capture/CaptureManifestV2.cs
      Capture/ArchivePackagerV2.cs
      Decode/Ft12IncrementalScanner.cs
      Decode/PassiveIec101Decoder.cs
      Analysis/PassiveExchangeCorrelator.cs
      Analysis/ObservedLinkActivityTracker.cs
      Analysis/RedundancyObservationAnalyzer.cs
      Analysis/ForensicFindingEngine.cs
      Replay/PassiveCaptureReplay.cs
    src/ARIEC60870.Core/Parsing/Iec10xAsduDecoder.cs (shared pure decode)
    src/ARIEC60870.Desktop/Features/MainWindow.PassiveMonitor.cs
    src/ARIEC60870.Desktop/ViewModels/PassiveMonitor*.cs
    tests/ARIEC60870.Monitor.Tests/*

## 3. Unidirectional API contract / capability security

Define IReadOnlySerialTap with ReceiveAsync, OpenAsync, CloseAsync, Diagnostics, Identity. **No** Write, SendBreak, ChangeRts, ChangeDtr, DiscardIncomingBuffer or transmit method. A separate serial implementation is needed because existing IByteTransport includes WriteAsync.

WindowsReadOnlySerialTap may internally use System.IO.Ports but:
- Handshake=None, software flow control disabled, RtsEnable=false, DtrEnable=false *when supported*.
- Do not drive TX/RTS/DTR onto site wiring; use physical NC output pins and an isolation/buffer tap. Windows opening a COM handle can momentarily alter control outputs; this is why software-only “passive” is insufficient.
- Never call DiscardInBuffer on active recording, except carefully defined pre-start preparation if explicitly labeled (default no discard). A COM driver can report noise/errors and cannot guarantee complete physical capture.
- Read independently per port, preserving bytes, in bounded rented buffers. Use a cancellation-safe close path; no Task.Run per byte, no delay per received byte, no per-byte logs.
- Fault of one port must not silently stop archival of healthy ports. Session becomes Degraded with source-gap boundaries and operator warning.

## 4. Pipeline and concurrency

One read pump per COM writes records into a single bounded ingestion path; one archival writer is the **durability authority**; one independent decoder/replay consumer may lag. The raw writer must not wait for a decoder or UI.

    UART1 RX -> ReadPump1 -> inbound chunks --\
                                                -> bounded serialized journal -> disk
    UART2 RX -> ReadPump2 -> inbound chunks --/
                                                    |                |
                                              asynchronous       replay/offline
                                              decoder feed       index/build
                                                    |
                                              bounded UI events -> WPF 75ms coalesced

A high-throughput design must avoid naïve allocations:
- byte[] blocks rented from ArrayPool<byte> (clear/return on every exit).
- Chunk metadata is small value-like event: session, stream, per-port seq, read tick, byte count, raw offset; retain bytes by pooled ownership until journal completes.
- A bounded System.Threading.Channels channel using an explicit overflow policy. **Do not silently use DropOldest/DropWrite**. On persistent full queue, atomically record loss/unknown interval; attempt bounded drain and transition CaptureIntegrity to Partial/Degraded, and continue capturing where feasible. If durability cannot be sustained, fail safe with a warning rather than green “Recording OK”.
- Batch async FileStream writes on a single writer task; do not flush to physical media per UART read. Flush on time/byte thresholds and Stop; retain periodic recoverable checkpoint records. No blocking disk waits on WPF thread.
- Decoder may run off the completed journal/append notifier, allowing playback and live views with independent backpressure. Never allow expensive string formatting or PDF generation on acquisition threads.
- Serial throughput budget must be calculated from actual baud and framing: bits/character=1 start + dataBits + parityBit(if any) + stopBits. Evaluate 2-port and 4-port worst-case sustained load and USB burst behavior. A 19,200 baud 8E1 line has 11 bits/byte, so one direction's nominal ceiling is ~1,745 bytes/s (not unlimited).
- Use metrics: bytes/chunks/frames per port, source sequence gaps, channel high-water mark, writer flush latency, disk remaining, integrity status, UI queue/slow flush.

Important limitations: bounded application queues prevent memory blow-up but do not create a magical no-loss guarantee. Even if app drops=0, a Windows USB driver may have lost unreported wire bytes; label evidence “No loss detected at application layer,” not “physically lossless.”

## 5. Timebase and ordering semantics

At process start record Stopwatch.Frequency, origin ticks, UTC origin, timezone/offset and platform clock source. Each ReadAsync completion stamps monotonic tick + UTC estimate and read start/end bound if practical. Every port has a monotonically increasing stream sequence and archive byte offset.

- Strict total order is guaranteed **within** each port by sequence.
- Cross-port merged timeline is *best effort*: read-completion timestamps can be skewed by USB batching and scheduling. In overlapping uncertainty intervals show “ordering uncertain”; never assert microsecond-level turnaround.
- Capture input chunk timing vs decoded frame timing: first/last known chunk reception bounds; link-to-link response latency is an estimate, not bit-level measured.
- Relay CP24/CP56 protocol timestamps belong to data events, not wire arrival times. Never substitute UTC receipt time as a valid relay timestamp.

## 6. Raw journal V2 binary format (proposed specification)

Write append-only temporary session directory first. Avoid creating/updating an entire ZIP per message. Use fixed-size bounded records with explicit version and checksums.

Files:
- session.meta.json, containing session ID, format version (ARIEC_CAPTURE_V2), build hash, UTC+monotonic timebase, site labels (optional), role mapping, per-port adapter IDs if available, electrical/cabling declaration, serial and field widths, status, loss counters.
- raw/stream-*.journal segments, append-only record format.
- indexes/frame-index.jsonl, append from analysis asynchronously, pointing to exact raw offsets.
- decoded/frames.jsonl, analysis/findings.jsonl, diagnostics.jsonl, user-markers.jsonl, optional readable report.md.
- validation/hashes.json (SHA-256 sizes for every finalized member) on packaging.

Journal record v1:
  magic (4 bytes) | version (1) | kind (1) | streamId (1) | reserved (1) |
  sequence (uint64 LE) | monotonicTicks (int64 LE) | streamByteOffset (uint64 LE) |
  payloadLength (uint32 LE) | flags (uint32 LE) | crc32c (uint32 LE) | payload bytes

Document every field's endian and CRC coverage before implementation; reject sizes beyond a configured per-record bound. Record kinds include RawChunk, StreamGap, DriverError, ClockAnchor, Checkpoint and EndOfStream. CRC32C is for local corruption detection; SHA-256 for final archive integrity. ZIP is created **only during finalization** from stable segments, written to a temporary destination, hash-verified, then atomically renamed. Segment rotation must not reset sequence.

Recovery:
- on crash, scan journal up to last complete CRC-valid record; preserve trailing partial bytes as a diagnostic, not as protocol traffic;
- reconstruct manifest with Incomplete=true, reason=UncleanShutdown;
- never overwrite an existing capture silently;
- maintain preflight disk capacity and warning thresholds; disk full -> explicit incomplete, stop archival safely, continue no misleading UI truth.

## 7. Incremental FT1.2 scanner / decode boundary

Each stream has its own scanner; do not merge raw stream bytes before parsing.

Algorithm:
- A byte is assigned one of: valid frame span, malformed frame candidate span, noise span, or pending incomplete span; all byte ranges refer to journal offsets.
- 0xE5 or 0xA2 represent single character responses, contextual meaning only.
- 0x10 candidates: expected length 4 + linkAddressWidth (0/1/2), verify 0x16 terminator and checksum.
- 0x68 candidates: validate two length bytes match, second 0x68, length range >= control+LA, exact 4+L+2 size, checksum and 0x16; upper bound protocol frame and configured cache. Reject impossible lengths before buffering unbounded data.
- Decode malformed candidates but keep all bytes and bounded resynchronization; every pass must advance or report “need more input”; deadline/size bounds for long incomplete fragments.
- Use Core/Parsing/LinkControlDecoder and the FT1.2 frame mechanics. **Beware** Core/Ft12Parser's current AsduDecoder is IEC-103. Extract framing-only logic or add a safe disable-ASDU path; then pass ASDU payload into the migrated IEC-10x decoder.
- Move Iec10xAsduDecoder from Master to Core in narrowly scoped transaction, with tests for all previously supported Type IDs and COT/CA/IOA widths. Do not add a second independent IEC101 decoder.
- Show unknown/unsupported ASDU as original bytes + recognized header, no invented point semantics.
- For balanced link modes, passive recognition should not reuse Master unbalanced role assumptions. Explicit PRM bits and source-port direction; “unsupported balanced semantic” when necessary.

## 8. Correlation model (P2)

Scope per (physical linkId, configured station link address, direction roles). Maintain bounded queues of unresolved requests and a reset/FCB observation model, not active controller FCB mutation.

- Candidate pairing requires *master-side request*, credible counterpart response, address/function compatibility, temporal plausibility and no obvious intervening reset. COT/ASDU matching is additional evidence where applicable.
- One-byte ACK/E5 is particularly ambiguous across addresses: correlate only when outstanding compatible requests can be uniquely justified. Otherwise “Ambiguous ACK” plus candidate IDs.
- Distinguish application GI lifecycle from the link-layer acknowledgements; do not call GI “complete” on ACTCON alone. ACTTERM or profile-specific evidence should be shown separately with confidence.
- Detect retries based on FCB, function/address/request identity, elapsed time and observed reset boundaries; not simply duplicate hexadecimal bytes.
- Record request/response estimated latency as interval bounds plus derived central estimate where justified; handle capture startup mid-transaction.
- User-configured investigation timeout thresholds are **analysis-only**. They never schedule polling or force a live link reset.

All findings contain: ruleId, severity, observation/inference label, evidence references (session/stream/raw offsets/frame IDs), scope, time range, explanation, alternate hypotheses and suggested external check. Confidence levels should not masquerade as mathematically calibrated probabilities.

## 9. Redundancy observation model

Maintain observed state per physical path, not ETERA controller state:

Unknown -> SeenStatusOnly -> SeenApplicationTraffic -> ApplicationSilent -> SeenApplicationTraffic

Maintain last request and last valid secondary, packet counts per function, response time trend, observed activity ratio, silence duration, resets and error windows. A/B comparison is enabled only if **both** redundant physical links have both RX directions actually captured.

On single-link tap: mark a local traffic-pattern transition and possible failover; no independent proof of traffic on unmonitored path. For a 30-second suspected cycle, analyze multiple episodes with time deltas and jitter; show exact evidence where request stops, retries start, and whether other path becomes application-active. Do not call status-only a fault.

## 10. Display and export contracts

WPF:
- Primary “Passive Monitor” workspace with 2 COM setup pane (initial), dense conversation trace, compact status strip and inspector. All Master controls hidden and inaccessible by command binding.
- “Freeze View” only pauses UI projection; “Stop Recording” finalizes journal; avoid ambiguous “Pause Capture”.
- Virtualization and 75ms batch dispatch kept. Stale UI is marked while acquisition status remains current.
- Config state saved under passive-specific namespace; never silently reinterpret existing IEC101 master setup.

V2 import:
- streaming reader verifies declared version, CRC/checksums, depth/size and decompression bounds; prevents ZIP slip and malicious input allocation.
- V1 import read path remains as-is with “selected/visible evidence; not guaranteed complete”.
- Exact schema names and public sample golden capture maintained via own reproducible generator, sanitized only.

## 11. Implementation sequencing to avoid trial-and-error

### Phase P0a — technical spike, 0 production wiring
1. Add Monitor project, typed options, read-only interface and simulated receive fixtures.
2. Add direct spool with safe timestamp/cancel/stop and integrity statuses.
3. Test two-port identical byte replay and injected driver failures. Confirm inability to send at compile-time/API boundary.

### Phase P0b — Windows serial receive (isolated bench)
4. Add WindowsSerialTap implementation, zero-output default, safe hardware open/close and explicit serial profile diagnostic.
5. Observe simulated physical two TX sources via isolated tap, confirm no original link degradation and no unexpected RTS/DTR onto site cabling.
6. Gate: no UI yet required for proving archive. Avoid styling work before archive proof.

### Phase P1a — frame decoding
7. Extract/migrate IEC-10x pure decoder to Core with unchanged Master regression behavior.
8. Add per-port scanner, raw spans, classification and frame replay tests; add strict malformed/noise fixtures.

### Phase P1b — first useful desktop deliverable
9. Add compact Passive Monitor tab/workspace using existing WPF virtualized trace and inspector patterns; hide active controls.
10. Implement V2 finalization/reopen and existing V1 compatibility; test simulated complete investigation session.
11. Build Windows x64 test artifact after consolidated tests, not per tiny code step.

### Phase P2 — analysis
12. Add passive request-response correlator and deterministic observation states.
13. Add “Investigate 30 s switching” engine with observed vs inferred evidence labels.
14. Add trigger markers, pre/post windows via indexed raw journal; one-click investigation package.

### Phase P3 — 4 COM upgrade
15. Generalize stream configuration (already modeled as N streams) to two independent redundant links. Require four independent RX sources for two duplex paths.
16. Compare A/B activity, failover inference and time uncertainty; no SCADA controller access implied.

### Phase P4 — field
17. Full 2-hour bench sustained coverage, cable/electrical validation and site-approved SICAM/ETERA capture review; regress no-change to active master.
18. Package verified runnable build, SHA256, known limitations, and actual engineering evidence.

## 12. Test matrix (required CI + hardware qualification)

| ID | Test | Pass criterion |
|---|---|---|
| M-001 | API/static scan | Passive assembly has no write/command API or Master session construction |
| M-002 | Two independent ports | Every generated raw byte recovered in correct per-port order |
| M-003 | Slow decoder/UI, fast input | Raw archiver retains exact bytes, bounded queues, UI cannot block receive |
| M-004 | Sustained bench ≥2h | No application-detected byte loss; stable memory; complete archive hashes |
| M-005 | Driver errors, USB unplug | Source marked degraded/incomplete; healthy source continues; Stop works |
| M-006 | Kill/restart during write | Journal recovered through last complete record; incomplete flag persists |
| M-007 | 10/68/E5/A2/noise/partial/bad checksum | Raw span union covers all bytes exactly and decode exceptions=0 |
| M-008 | 101 decoder migration | Existing Master 101/104 golden vectors unchanged, IEC-103 unaffected |
| M-009 | Serial field sizes 0/1/2, COT 1/2, CA 1/2, IOA 1–3 | Correct parser or clear unsupported/unknown, no silent false decoding |
| M-010 | FCB retry, reset, ambiguous ACK | Confidence-qualified conclusions with evidence IDs |
| M-011 | Status-only standby normal | No false failover/fault alert |
| M-012 | Recurrent 30 s 4-port fixture | Multiple episode detection, correct link and witness evidence |
| M-013 | 2-port single-link fixture | No invented assertion of unseen Link B |
| M-014 | V2 export/import + corruption | Exact raw restore, SHA256 fail on tamper, safe bounded reader |
| M-015 | V1 existing capture | Unchanged import; clearly marked limited retention |
| M-016 | Electrical commissioning | Passive tap does not interrupt handshake, change bit rate, degrade SCADA traffic or add driver control assertions to connected plant |
| M-017 | Performance on modest PC | Bounded memory/UI row count, no per-byte WPF dispatcher, perf recorded against defined test machine |
| M-018 | Repeated start/stop 100× | No leaked COM handles/tasks, consistent final manifest |

Fixture builder must emit deterministic raw bytes and exact expected decoded observations, with corrupted versions; no access to a real SCADA/RTU is required for continuous regression. Real instrument validation remains separate.

## 13. Gating, backward compatibility and safe release

- No changes to production Active Master polling/dual-link state machine. Decoder migration gets isolated focused regressions first.
- Keep release flow artifact verified and CI-efficient: consolidate related changes into meaningful PR milestones rather than a CI run per small tweak.
- New feature defaults disabled; opening existing saved 101 master setup must behave identically.
- Record no-physical-probe limitation and missing CTS/DCD lines in capture manifest and report.
- Before merging code: unit and regression green + Windows package smoke; before recommending live tap: engineering-approved circuit/ground/isolation review.
- Protocol inference never overrides source evidence or silently repairs a damaged capture.

## 14. Review triggers / architecture decisions requiring explicit re-evaluation

- Real field topology is RS-485 (half-duplex bus) rather than RS-232 full duplex: stop and design separate isolated receiver hardware/line topology. Two RX COM topology is then not automatically valid.
- Need carrier/RTS/CTS/DCD transition timeline: standard 2-COM tap cannot supply it. Add hardware sensor data source with timestamp uncertainty.
- Disk writer cannot sustain specified load: do not enlarge memory without bound; measure OS/USB/capture path, reduce optional decoding, adjust bounded batching and indicate integrity degradation.
- IEC-101 malformed stream parser ambiguous on unknown widths: preserve raw, show candidates/unknown, require explicit profile; do not guess a passing checksum interpretation into an asserted device identity.

## 15. Citations and scope

independent IEC-101 reference reference documentation:
- Manufacturer product and capabilities: independent protocol reference
- Manufacturer user manual (public PDF): independent protocol reference ; especially §6 message capture/import, §7 UI and two serial ports, §8 Line Monitor / Messages / Line Analyzer, §13 RS232 monitor cable, §IEC 60870-5-101 quick start.
- Manufacturer knowledge base: independent protocol reference

No certification, verified performance parity, or physical fidelity with independent IEC-101 reference/BCOM-USB is claimed. This document defines testable independent product targets.
