# ARD — Virtual Online Sniffer: duplex virtual-serial proxy with independent byte evidence

**Date:** 2026-11-06  
**Status:** Proposed / **NOT IMPLEMENTED**. Architecture decision for `VOS-01..16` in the [PRD](VIRTUAL_ONLINE_SNIFFER_PRD_20261106.md). **2026-11-07 correction: existing IEC-101 Monitor workspace only; Virtual Inline is an input source option, not a new window or top-level mode.**  
**Bench:** [VSPE four-port setup and test gate](VIRTUAL_ONLINE_SNIFFER_VSPE_FAT_20261106.md).  
**Non-negotiable:** preserve existing IEC101 physical RX-only passive monitor and canonical IEC101/103/104 decoder/observation truth; reconcile open PR #95 before implementation.

## 1. Decision and alternatives

**Decision VOS-ADR-01:** implement a **user-mode, bidirectional, byte-transparent inline proxy** as an **alternative transport source of the existing Monitor**, between two **independent VSPE Pair devices** in one Windows process. Do not build or ship a kernel virtual serial driver. A Pair has one external application endpoint and one ARIEC relay endpoint, avoiding competing opens of an ordinary exclusive COM port.

    Master app (COM11) <== Pair A ==> (COM12)
                                        | ARIEC VOS
    Slave  app (COM14) <== Pair B ==> (COM13)

    Pump A -> B: read COM12 -> attempt/write COM13 -> Slave COM14 receives
    Pump B -> A: read COM13 -> attempt/write COM12 -> Master COM11 receives

**Evaluated alternatives:**
- Two VSPE **Connector** devices: one numbered virtual COM can be opened twice; valid VSPE-defined alternative, less discoverable ownership and compatibility differences. Do not hardcode Pair-only assumptions in the low-level relay interface.
- **Splitter + Bridge**: useful externally but adds multiple targets and dangerous slow-reader backpressure/possible loss. It is a manual VSPE diagnostic alternative, not the V1 product architecture.
- **com0com** pairs: can be tested as independent alternative driver; signed-driver/installation requirements vary by Windows installation. No dependency or GPL source bundling.
- Windows kernel filter/minifilter/serial interception: elevated complexity, signing/security burdens, incompatible with product's first user-owned virtual bench use case.
- Byte forwarding only after successful FT1.2 parsing: **rejected**, because malformed/raw traffic would disappear or become modified and diagnosis would fail.
- Running Virtual Online through `WindowsReadOnlySerialTap`: **rejected**, because this class intentionally has no Write contract.

## 2. Explicit module ownership

| Layer | Responsibility | Forbidden |
|---|---|---|
| `ARIEC60870.Core` | Existing FT1.2 scanner, ASDU decoder, typed trusted/invalid representation | Any COM driver, relay scheduling, GUI or duplicate FT1.2 parser |
| New `ARIEC60870.VirtualRelay` project (preferred), or isolated Monitor transport folder if project cost justified | `VirtualSerialRelaySession`, `IByteDuplexEndpoint`, per-direction pumps, fail-fast ownership, explicit byte counters, transport diagnostics | GI, polling, IEC decision-making, WPF updates, silent byte mutation, unbounded queues |
| `ARIEC60870.Monitor` | Existing passive interpretation/journal/Source indexing adapted via an **observation-only sink interface**, without using RX-only tap as proxy | Opening active TX on existing passive tap; assumption that virtual relay is physically passive |
| `ARIEC60870.Desktop` | **Existing MainWindow IEC-101 Monitor Setup and tabs**: add a physical/virtual source selector with contextual port rows and Start labels; reuse existing Trace, inspector, Values/Events, report and evidence exporter **without a second window** | Serial read/write loop, ASDU parsing, per-byte Dispatcher events, new protocol-mode dropdown entry or a second timeline authority |
| Test harness | In-memory duplex endpoints, test streams, fault injection, optional real VSPE bench script | Claim real driver tested from in-memory CI |

Suggested contracts (pseudotypes; decide naming with actual current source before coding):

    interface IByteDuplexEndpoint : IDisposable
      PortIdentity
      Open()
      Read(byte[] buffer, int offset, int length) -> int  // 0 = normal bounded idle timeout
      Write(byte[] buffer, int offset, int length) -> void // successful completion is NOT remote receipt
      Close()
      ErrorCounts

    interface IRelayObservationSink
      ObserveIngress(epoch, direction, sourcePort, chunkOffset, bytes, hostTimestamp, tick)
      ObserveWriteAttempt(epoch, direction, byteCount)
      ObserveWriteCompletion(epoch, direction, byteCount)
      ObserveFaultOrGap(epoch, direction, stage, countOrUnknown, error)

    sealed VirtualSerialRelaySession
      Start(ValidatedTopology)
      Stop()
      SessionProgress
      CompletionResult

**Rule:** never feed an *egress copy* back through the decoder as a second physical frame. One ingress source stream per direction, one immutable source identity with stable epoch/offset; egress completion is correlated metadata, not a duplicate observed frame.

## 3. Source identity and forensic accounting

Each session gets immutable `sessionEpoch` and two directions:

- `MASTER_TO_SLAVE`: ingress COM12, egress COM13.
- `SLAVE_TO_MASTER`: ingress COM13, egress COM12.

Every acquired chunk records source ID, monotonic read ordinal, offset, byte length, host UTC and monotonic ticks, optional checksum and driver error observation. Treat system time as Windows arrival time **not UART bit time**. It is not a synchronized electrical line analyzer.

Counters for *each direction*:
- `ingressReadBytes` (actual user-mode reads);
- `egressWriteAttemptBytes`;
- `egressWriteCompletedBytes` (driver success, **not peer consumption**);
- `egressWriteFailedOrUncertainBytes` (include current ambiguous in-flight chunk);
- `rawObservationAcceptedBytes`, `rawObservationDroppedBytes` or `integrityUnknown` if the bounded observation path cannot retain all input;
- `decodedGoodFrames`, `decodedBadFrames`, `unframedBytes` based on the **shared** decoder;
- `portErrors`, `timeoutsWhileWriting`, `recordingErrors`, `maxQueueDepth` if a queue is present.

Invariant on a completed healthy test: ingress concatenation equals successful egress input stream **and** bytes externally read by peer; assert three independent streams from the test harness. Runtime equality `ingressReadBytes == egressWriteCompletedBytes` is useful but does **not** prove VSPE/peer delivery. Partial write or close may leave an *unknown* interval, never write-complete or replayed by assumption.

## 4. Relay pumps: minimum necessary complexity

Start two isolated workers:

    Pump A:
      read(COM12) -> capture ingress metadata -> write(COM13) -> write-completion evidence
    Pump B:
      read(COM13) -> capture ingress metadata -> write(COM12) -> write-completion evidence

- Use *binary* `SerialPort.Read(byte[],...)` / `Write(byte[],...)` and pooled fixed-size buffers. Never `ReadExisting`, line/newline, encoding conversion or text streams. Chunk boundaries are not IEC frame boundaries.
- One reader per ingress and one writer per egress, so each direction is FIFO. Read and write simultaneously on the same COM endpoint from opposite worker is expected for full duplex; verify selected driver behavior in bench.
- A bounded serial `ReadTimeout` (initial investigation candidate 100-200 ms) treats ordinary idle as **zero new bytes**, not as disconnect. A finite write timeout prevents indefinite stuck writes and triggers an explicit partial/fault state. Exact values are benchmark-tuned, not protocol specs.
- Dedicated long-lived worker per direction, no `Task.Run` per byte, no threadpool per chunk, no UI wait. Use cancellation and close to break blocking driver I/O; bound joins and expose a force-close path if driver will not release. Avoid unsafe `Thread.Abort`.
- The simplest design is direct read->write with *no intermediate relay queue*. It naturally bounds backlog and ensures ingress is not read indefinitely beyond a stalled egress. If a queue is later necessary, it MUST be bounded and **never use DropOldest/Newest/Write for authoritative relay bytes**. Queue saturation: mark `Faulted`, stop forwarding, report in-flight/unknown delivery; never call it successful flow.
- If a driver or peer is slow, blocking/timeout is an observable performance failure; optimizing away backpressure by silent dropping is unacceptable.
- No automatic retry of a write after an **uncertain** driver failure: duplicate IEC101 commands/events would be worse. Restart is a fresh epoch, not a hidden replay; user explicitly starts again.
- Record ingress before attempting egress, **without blocking forwarding on parsing or WPF**. Capture and GUI subscription use a bounded observer path. If the observer cannot retain some bytes, report `Capture Partial` with gap interval; relay may continue while still healthy, as configured. The observation sink must never hide a gap or exert unbounded backpressure. For a separately requested strict forensic capture policy, failure must be conspicuous and may stop the relay.
- Serial APIs do not expose a general guarantee that remote application opened/read the Pair endpoint; do not display `Peer Connected` unless driver notification is explicitly supported and confirmed. Show `COM Open / No traffic observed` otherwise.

## 5. Reliability lifecycle / state machine

    Idle -> Validating -> OpeningA -> OpeningB -> RelayReady -> TrafficObserved
                                |                      |             |
                                +-- FailedCleanup <---+--- Faulted <-+
                                      |
                                  Stopped
    RelayReady or TrafficObserved -> Stopping -> Stopped

Start:
1. Validate two different names, availability, unsupported control lines, UART profile, capture policy, unsupported cross-protocol mode and local session ownership. No implicit active Master launch.
2. Create **both** endpoint instances and open each **exactly once** (in either consistent order); if second fails, close first before reporting failure. Do not start pumps until both open.
3. Allocate an immutable epoch, two direction counters and observer channel; initialize source headers with topology and configuration.
4. Start both pumps and publish `Relay Ready: COM handles open; remote apps not verified`. Traffic confirms byte ingress only.

Stop / failure:
1. Cancel both pumps; close both serial handles to unblock reads/writes; quiesce publisher then finalize optional raw journal.
2. If one direction encounters read/write failure, **fail closed for both directions**: no surviving one-way "healthy" bridge or automatic COM reopen. Mark uncertain in-flight bytes and incomplete stream evidence.
3. Counters survive stop for inspection; new Start resets only through a **new** epoch. Stale task callbacks are ignored by the GUI/evidence reducer.
4. Crash/forced stop can interrupt a chunk: mark journal partial, do not claim complete flush or peer receipt.

Do not infer peer availability from `Open()`; waiting for a peer is not a reason to spin indefinitely or to fabricate an IEC connection-success message.

## 6. Observation, framing and canonical reuse

The proxy must forward bytes **before** or independently of decoder results. Fork immutable ingress chunks into the existing canonical FT1.2 byte-oriented source pipeline through a new observation adapter:

    Read source bytes
        |-- Relay to opposite COM (critical path)
        +-- bounded observation queue
               +-- raw spans / optional spool + provenance
               +-- shared Core FT1.2 framing + ASDU decode
               +-- typed source-owned facts -> existing Trace / Values / Events / Export

Important:
- The same stream can split an FT1.2 frame across arbitrary reads; Core scanner keeps per-direction incremental state and resynchronizes on invalid input.
- Preserve every accepted input byte as raw-span evidence **if recording is enabled**; if live-only UI cannot render every raw byte individually, aggregated unknown/noise spans and byte counters must be accessible and never represented as valid decoded frames.
- Trace remains one physical (virtual-line-observed) frame per row and source. E5 remains original byte and contextual interpretation only; no invented FT1.2 header.
- Share typed parser, trust admission and optional process-event ledger with existing Monitor; do not use Master sender logic. PR #95 changes to observer projection must be reconciled before an implementation PR.
- Preserve source/capture identity in data exported from VOS. Distinguish `Virtual Inline` from `Physical RX-only` in manifests and filenames; don't silently mislabel existing `.ariec` semantics.

## 7. Clocking, emulated baud, handshake and write semantics

A virtual Pair can deliver data at a speed unrelated to configured UART bps unless the driver is explicitly set to emulate baud. Thus `baud=1200` alone is **not proof** of 1200-bps wire timing. The app may validate matching UART settings for deterministic tool configuration but must describe timing as *virtual-driver host timing*. Measure with/without VSPE `Emulate baud rate`; include driver/version and option in the benchmark metadata.

V1 supports `Handshake.None`, no modem-register forwarding across both proxy hops, no hardware break forwarding, and no RS485 transmit enable. Do not assert `RTS/DTR` as an implicit "connected" trick. Explicitly warn when external apps require them. VSPE Bridge documentation independently notes generic bridge does not forward modem registers; VOS should not promise more.

Microsoft's `SerialPort.DataReceived` is not guaranteed once per byte and events/errors can be reordered. Therefore acquisition uses dedicated binary reads, measured driver-error counters and no `DataReceived`-driven frame truth.

**VSPE Pair option hazard:** `Allow buffer overruns` may acknowledge writes when receiving buffer is full. For byte-fidelity tests require it **OFF** and do not enable other silent-drop configurations. A completed `Write` remains only an OS/driver result.

## 8. Preflight diagnostics and minimal UI

Preflight checks validate configured ports and supported mode without trying to hijack either external app's endpoints. A separate optional **Open test** is active and must disclose it will acquire COM handles. Port display names and driver metadata are hints, not guarantees that two valid pairs are wired; user confirms Master/Slave owner COMs.

Inside existing Monitor's compact Setup, the only new choice is `Source: Physical RX-only / Virtual Inline`. When Virtual is selected, show `Master-side COM12 | Slave-side COM13 | UART 1200 8E1 | Start Relay & Monitor`; when Physical is selected, retain present `SCADA TX RX | RTU TX RX | optional Link B | Start RX`. Below: `M→S IN, OUT, Fault` and `S→M IN, OUT, Fault`, label `Driver Write Complete ≠ Peer Read`, a normal live Trace and selected raw byte inspector. No 2nd large dashboard or new parser window.

Unknown frame must be observable via original HEX even if the ASDU is unrecognized. Zero-byte state must show exact COM names, open status and a next diagnostic. WPF AUTO/HOLD affects **viewport only**; never alter pumps/recording.

## 9. Testing and fault matrix

| Fault/test | Expected invariant / verdict |
|---|---|
| A->B 0..65536 arbitrary binary with split writes; B->A concurrently | No reorder/cross-talk; peer byte hash identical to input |
| E5, 10, 68, A2, 00, FF, random, invalid checksum/length, back-to-back FT1.2 | All byte sequences forwarded unchanged; decoder may call them malformed/unknown |
| Peer app absent when relay starts | `RelayReady / Peer unverified`, no false connected or silent success claim |
| COM13 occupied / only COM12 opens | Fail preflight/open; COM12 handle released |
| WriteTimeout / driver error / slow egress | Both directions stop or fault, never silently drop/retry uncertain chunk |
| Observer queue full / WPF thread stalled | Transport continues when configured capture-degrade; explicit capture gap and counters; no hidden loss |
| Full journal disk full / power loss | Partial integrity; repair/reopen path must not promise complete archive |
| Stop during blocking read/write | Both handles closed, Start available, no stuck controls |
| 100 rapid Start/Stop with distinct epochs | No stale frames, cross-session counters, leaked readers or writes |
| External app sends IEC command/GI, negative response or no response | No ARIEC command insertion; only observed raw bytes and independently interpreted status |
| 1200-bps emulation and high-throughput burst | Quantify timing/CPU/queue/host uncertainty; no forged bit timestamps |
| 50k generated mixed frames and 2k/5k/50k byte raw cases | Fixed-size memory budget, deterministic exact source counters, no UI slowdown-induced relay mismatch |

Use both **fast in-memory unit/integration** checks and **real Windows VSPE Pair** acceptance. Windows GUI/COM drivers cannot be proven by GitHub Linux unit tests. The CI relay tests must inject byte/timeout/partial/close errors, and include a console probe independent of WPF; only user-run Windows bench can prove the VSPE wiring and peer behavior.

Benchmark metrics: OS/build, VSPE version/options, COM map, third-party simulator versions, baud setting/emulation, record policy, total generated/received/forwarded bytes per direction, SHA-256 of sent and peer-read streams, mismatches, failures, CPU/working set/allocations, latency p50/p95/p99 and precision limit. **Zero mismatches on tested setup** is a bounded proof, not a universal lossless guarantee.

## 10. Implementation sequence, branch discipline and approvals

1. **Design/documentation (this change):** align PRD, ARD, bench; no production code or feature claim.
2. **Core relay + test harness:** project/isolated namespace with two pumps, one explicit observation interface into the **existing Monitor receive authority**, lifecycle, counters and fake endpoints; no new WPF screen, active Master or passive-tap changes. One meaningful implementation PR with negative tests, not patch stacks.
3. **Canonical evidence integration:** introduce a narrow source-adapter contract shared by physical `PassiveMonitorSession` and new Virtual Inline ingress. Reuse current Core FT1.2 and Monitor writer-owned journal through this adapter; reconcile PR #95 before editing its source-fact or WPF projection logic. No duplicate parser, event ledger or WPF projection.
4. **Existing Monitor Desktop + VSPE FAT:** add **Source** picker to already-present Monitor Setup, preserving old physical defaults, existing left rail/Trace/Values/Events/Report and Stop; real two-Pair 4-port bench verifies observed original HEX even when decoder fails.
5. **Release candidate:** one selective Windows workflow + visual check + actual user test, SHA-pinned artifact, truthful status per gate. Do not churn expensive CI for docs-only changes.

## 11. Primary technical references

- VSPE architecture: https://eterlogic.com/help/vspe/index.html
- VSPE Pair two-port behavior/options: https://eterlogic.com/help/vspe/DevicePairPage.html
- VSPE Connector (alternative): https://eterlogic.com/help/vspe/DeviceConnectorPage.html
- VSPE Splitter slow-reader and data-loss risk: https://eterlogic.com/help/vspe/DeviceSplitterPage.html
- VSPE Bridge limitations: https://eterlogic.com/help/vspe/DeviceBridgePage.html
- .NET SerialPort and raw byte methods: https://learn.microsoft.com/en-us/dotnet/api/system.io.ports.serialport
- .NET DataReceived limits: https://learn.microsoft.com/en-us/dotnet/api/system.io.ports.serialport.datareceived
- .NET bounded channels / backpressure: https://learn.microsoft.com/en-us/dotnet/core/extensions/channels
- .NET SerialPort.Close semantics: https://learn.microsoft.com/en-us/dotnet/api/system.io.ports.serialport.close
- Open virtual-pair alternative, conceptual reference only: https://com0com.sourceforge.net/

Published behavior guides design. No third-party code, proprietary SDK, driver, screenshot, licensed IEC text or private capture may be pasted or bundled.


## 12. ADR-02 — one shared Monitor workspace, interchangeable acquisition (2026-11-07)

**Decision:** The `IEC-101 Passive Monitor` UI/workspace is the sole operator-facing IEC-101 Monitor. Its setup adds **`Capture source: Physical RX-only | Virtual Inline`**; neither choice creates a new window nor a new top-level protocol option. Physical remains the default for backward compatibility. This supersedes any earlier wording in this ARD that implies a separate VOS window or protocol mode.

### Boundary and abstraction

    Existing MainWindow IEC101 Monitor
       Source selector: [Physical RX] [Virtual Inline]
                    |
       existing Capture/Trace/Values/Events/Report UI
                    ^
           canonical Monitor facts & event ledger
                    ^
             canonical Core FT1.2/ASDU
                    ^
          per-source immutable byte ingress
                 /           \
      Physical RX taps     Virtual Inline A/B
       (Read-only)         (Read + Write relay)
         2/4 COM               2 COM in VSPE pairs

Preferred contracts:
- `IObservedByteSource`: exports read-only `ObservedByteChunk(epoch, circuit, direction, physicalOrVirtualProvenance, sourceCom, sourceOffset, bytes, hostTimestampQuality)` plus `SourceFaultOrGap`; **never exposes Write to Core or UI**.
- `PhysicalRxSourceAdapter`: projects existing 2/4 RX passive byte ingestion, preserving stream IDs and electrical safety invariants.
- `VirtualInlineSourceAdapter`: uses two duplex serial endpoints to relay **original bytes**, and projects ingress chunks exactly once into the same canonical monitor path.
- `CanonicalMonitorEvidenceProcessor` (existing owner refactored only as required): single FT1.2/ASDU, trust, transaction, GI, SOE, Values, report and original-frame lineage. **Do not start separate independent event states** in each adapter.
- `MonitorSessionCoordinator`: owns one source per session and its immutable epoch; switching source must close/finish prior session and cannot silently reconfigure an active capture. Existing physical 4-RX A/B profiles remain available only in Physical.

**Important implementation detail:** Current `PassiveMonitorSession` likely combines reading, framing and journal. Reconcile its current code and open draft #95; extract a narrow read-to-framer seam only if proven safe. **Do not rewrite the working passive capture engine wholesale** merely to introduce interface abstractions.

### Laboratory parity as mandatory engineering proof

Test harness feeds **identical synthetic raw byte vectors** to:
1. The shared canonical input from `PhysicalRxSourceAdapter` (injected test endpoint; no plant).
2. The shared canonical input from `VirtualInlineSourceAdapter` (injected duplex endpoints; one forwarded copy in each direction).

Assert equality of normalized per-direction byte streams, offsets, FT1.2 checksum/framing validation, link-layer FC/PRM/FCB/FCV/ACD/DFC, E5 contextual handling, COT/CA/IOA/quality, GI/command lifecycle, event facts and original HEX across Trace/Values/Events/Report. Compare after normalizing provenance, source/epoch IDs and host arrival timestamp (which vary legitimately). **Transport accounting must remain different**: Physical TX=forbidden; Virtual has write-attempt/completion and uncertain-delivery facts. No UI-dependent backpressure or hidden "refresh".

Include deterministic reproducible incident cases: zero-byte COM with correct open, only one stream producing data, invalid parity/width hypothesis (without fabricated repair), broken/truncated/chunked FT1.2, E5 ambiguity, partial GI, SOE raw-in/canonical-present-but-UI-missing, rejected ASDU, queue gap, reconnection and stale epoch. Existing archive replay without new live endpoints is the safest way to reproduce earlier field data if captures are available.

An optional **tiny lab peer harness** can generate IEC-101 link/GI/SOE/command and bad frames over the VSPE pairs; it is strictly a test fixture and must not be automatically attached to the Monitor production receive path or masquerade as a real SCADA/RTU. No complete third-party SCADA stack required for basic lab regressions.

### Exact field-validity boundary

**Proved by virtual**: software COM forwarding, byte preservation on tested VSPE version/settings, canonical decode and view/report parity, application-level behavior on supplied test patterns, bounded queues, tested UI state and restart.

**Cannot be proved by virtual**: TX/RX electrical wiring isolation, RS232/485 level/loading/ground, RTS/CTS handshake in real equipment, UART parity/framing errors generated in physical receivers, field USB driver buffering, real baud-rate idle/bit-edge timing, actual SCADA or RTU interoperability/response semantics. A field return requires independent physical FAT and genuine raw capture; a perfect virtual test must be labeled *Lab Verified / Field Pending*.

### Implementation acceptance before merge

- No new top-level `ProtocolModeComboBox` item or second Window; only source controls in existing Monitor Setup.
- Existing `WindowsReadOnlySerialTap` interface retains no `Write`; no active relay COM opened under Physical selection.
- Exactly one trusted frame/event publisher per ingress frame and one immutable source/epoch identity; egress byte copies do not produce duplicate SOE.
- Full original HEX on both sources even if frame malformed or undecodable, with observable gap/driver-error/unknown state; no synthetic protocol success.
- Cross-adapter golden parity fixture plus 100 Start/Stop, concurrent 2-direction traffic, occupied COM, forced close, timeout, overload, journal failure; UI filter/HOLD cannot change canonical ingest.
- Real Windows VSPE two-Pair bench and true hardware FAT are **separate gates**. Neither is presently completed.
