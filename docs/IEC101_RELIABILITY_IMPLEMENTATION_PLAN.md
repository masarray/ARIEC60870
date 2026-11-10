# IEC-101 P0 — Reliability, Interoperability & Transaction Architecture

**Status:** engineering implementation contract / planned work; **NOT an implementation or field-conformance claim**  
**Reviewed:** 2026-11-11 · **Owner/tracker:** [P0 issue #143](https://github.com/masarray/ARIEC60870/issues/143)  
**Baseline observed:** `master` at `f99627f3ac252885d411985a29f154e221577784`, public prerelease `v3.6.8-rc.3`; draft [serial PR #136](https://github.com/masarray/ARIEC60870/pull/136) at `e5b6634ac2bd92312f633c7a779e8da62f5bc61b` (**14 ahead, 3 behind master** at design review). Re-check live refs before editing.  
**Scope:** IEC 60870-5-101 **active unbalanced single-link and dual A/B**; shared link exchange rules, serial fault handling, scheduler, GI, commands, diagnostics and field proof. **Not:** IEC-101 balanced active TX, production SCADA qualification, unapproved RTU commands, IEC103/IEC104 rewrites, or active TX in passive monitoring.

## 0. Authority and ownership

This is the **IEC-101-specific P0 implementation plan**, subordinate to the cross-product [IEC10X PRD](IEC10X_INTEROPERABILITY_PRD_20261029.md), [IEC10X ARD](IEC10X_INTEROPERABILITY_ARD_20261029.md) (ADR-01/02/03, R6–R9), [NUC research](IEC10X_NUC_RESEARCH_20261029.md), and [AGENTS](../AGENTS.md). It *details* the IEC101 link reliability work; it does **not** cancel the R6 Event Journal, WPF evidence or capture boundaries. If a decision conflicts with an existing ADR, record a reasoned amendment and test before implementation.

**Truth hierarchy:** authorized IEC 60870-5-101 edition + signed project/vendor interoperability agreement for normative behavior → observed original physical bytes and timing → validated typed frame/exchange evidence → runtime state and diagnostic interpretation → UI/report. A synthetic test cannot prove physical wire compatibility. Do not copy licensed standards text, vendor tables or external stack code.

Keep **active Master** ownership in `src/ARIEC60870.Master`, canonical FT1.2 parsing in `src/ARIEC60870.Core`, independent **receive-only Passive Monitor** in `Monitor`, and WPF as a projection of typed facts only. No second decoder, WPF-owned FCB, hidden station-reset policy or competitor-derived source code.

## 1. Current baseline and gaps (evidence levels)

| Area | Verified at design review | Gap / risk | Requirement |
|---|---|---|---|
| `master` IEC101 single-link | Unbalanced master polls, GI and existing trace; existing FT1.2 decode | Response acceptance can lack pending-FC-specific correlation; uncertainty and FCB/recovery behaviors need parity tests | R01–R04 |
| Draft PR #136 | FC-specific validator, DFC busy gate, baud-aware serial timing, safer command ownership and A/B failover, RX-byte diagnostics; Windows CI passed at its head | **Not in public RC**; stale relative to current master; FC1 NACK, delayed response, partial reader lifetime and robust retry need additional red fixtures | R00–R10 |
| Single vs A/B | Two working orchestrators | Shared transaction invariants not yet one authoritative per-link policy; potential divergent handling | R01/R08 |
| Serial Windows | Open/close, configurable RTS/DTR/Handshake, separate RX/TX diagnostics | `SerialPort.Write` blocking/cancel semantics, unknown driver RX, cut-off frame, unplug/reopen and stream reuse | R05–R07 |
| Profile | Versioned `Iec10xInteroperabilityProfile`, explicit Generic/optional NUC label | Effective runtime contract and validated project overrides require further work | R12 |
| Field observations | IEC_Test has communicated with test RTU; ARIEC observed FC11 out-of-context in one test and TX-only in a later session | Physical COM identity, electrical interface, CTS/RS232 engineering mode and cable/adaptor equality have **not** been proven; no grounds to claim SICAM nonconformance | R14–R16 |

**Non-claims:** neither simulator/CI green nor a COM handle opened nor a decoded FC11 proves GI data flow or correct operating state. UI reports must distinguish `Implemented`, `Automated-tested`, `Independent-peer tested`, `Physical FAT tested`, `Unknown`.

## 2. Target architecture — one link transaction authority

```text
  Desktop Setup / Connect / Operator intent      (no protocol ownership)
                     |
  IEC101 Session Coordinator                 (lifecycle, GI, class polls)
           |                       |
  Single-link coordinator        A/B redundancy coordinator (active ownership)
           \                       /
          Iec101PerLinkRuntime [one per physical link/connection epoch]
            |  TransactionEngine -> ExchangeResult -> scheduler
            |  FCB/FCV + link health + DFC/ACD + retry policy
            |  one persistent RX owner / FT1.2 stream decoder
            |  serialized, bounded TX owner + SerialPort transport
            |
  Immutable source-qualified bytes -> frame -> exchange -> ASDU ->
  command/GI/event verdict -> bounded evidence store -> WPF + PDF
```

**Implementation choices:** preserve `Core/Parsing/Ft12IncrementalFramer.cs` and existing `Master/Protocol/Ft12StreamReader.cs` as canonical; adapt rather than rewrite. Shared per-link exchange policy is a **pure/testable core**; integrate into `Iec101MasterSession.cs` then `Iec101/Redundancy/Iec101DualLinkChannel.cs`. `Iec101DualLinkRedundancySession.cs` owns only failover/failback/role, not its own alternate response truth. Introduce only cohesive components with real ownership (proposed `Iec101ExchangeContext`, `Iec101TransactionEngine`, `Iec101ExchangeOutcome`, `Iec101SerialTiming`), not a forest of overlapping services.

### Invariants (must be enforceable in code and tests)

- **I-01:** Exactly one reader and one serialized TX owner per physical COM epoch. At most one **confirmed request/response exchange** in flight per link. Do not multiplex a new service over unresolved same-link response ambiguity.
- **I-02:** Every received byte is attributed to physical source and epoch **before** protocol parsing. Invalid, noise, echo, partial and unknown data are preserved or explicitly counted with integrity status. WPF is never the evidence ledger.
- **I-03:** A secondary reply can complete an exchange only after checksum/frame format, `PRM=0`, valid address, correct pending primary FC/service, applicable FCB/FCV and epoch checks pass. Do not treat FC11 as GI/Class reply. `E5` has no address/ACD/DFC and cannot establish initial link identity from silence; `A2` must not be reclassified as a valid IEC101 acknowledgment.
- **I-04:** FCB is scoped to a per-link connection generation and only advances on a matching, accepted positive outcome under the verified profile; NACK, malformed, out-of-context, busy and ambiguous timeout never advance it. Reset/synchronization is an explicit, profile-approved link procedure.
- **I-05:** For *eligible read/poll* retries preserve original frame + FCB and apply standard-conformant bounded retransmission **only when safe**. For clock sync, setpoints, physical controls and any potentially mutating uncertain delivery, **no automatic replay** (including across A/B failover). ACK at link layer is **not** process execution proof.
- **I-06:** DFC=1 means remote busy, not COM disconnection. During busy, permit only the profile-appropriate bounded status/supervision path (FC9), not GI/Class/command. ACD indicates pending Class1 data but does not override DFC.
- **I-07:** An unverified/suspect/demoted link never dispatches queued mutating controls. Queue bounded at enqueue with rejection reason; `LinkEpoch` and active owner checked both at admission and immediately before wire send. No stale command survives owner change.
- **I-08:** Source-qualified GI, commands, events, timestamps and quality remain independent across A/B, and do not borrow success evidence from other sources or previous sessions.
- **I-09:** Recovery does not automatically flip RTS/DTR, parity or baud, reset a production outstation, alter profile widths, or synthesize a GI completion.
- **I-10:** Cancellation/Stop closes ownership and resources deterministically; no RX task from a previous epoch can write state or emit a new request after Stop.

### Proposed explicit exchange state model

```text
Created
  -> Admitted (profile, link health, owner and queue space checked)
  -> TransmitAttempt (original TX bytes, owner, FCB/FCV, monotonic deadlines)
  -> AwaitSecondary
       -> ConfirmedPositive
       -> ConfirmedNegative
       -> RemoteBusy
       -> UnexpectedSecondary / Malformed / Truncated
       -> TimedOut / TransportFailed / Cancelled
  -> Completed (typed result; link FCB state transition if and only if permitted)
```

Outcomes are not conflated:
`ConfirmedPositive`, `ConfirmedNegative`, `Busy`, `NoData`,
`TimedOut`, `OutOfContext`, `FrameInvalid`, `UncertainDelivery`,
`RejectedNotReady`, `RejectedStaleOwner`, `Cancelled`.

Some IEC101 requests have no unique wire transaction identifier; a late otherwise-plausible reply may be **intrinsically ambiguous**. Preserve `UncertainDelivery`/unverified instead of inventing correlation.

### Response matrix contract

| Primary service | Positive/negative secondary interpretation | Acceptance rule |
|---|---|---|
| FC9 link status | Addressed secondary FC11 status; negative/status errors as authorized by selected profile | FC11 with matching address and fresh epoch is **link-status evidence only** |
| FC3 confirmed user data | Allowed confirmation format(s) per authorized IEC edition + selected interoperability profile | Link ACK does not prove GI ACTCON/ACTTERM or physical command operation |
| FC10 Class 1 / FC11 Class 2 polls | FC8 user data, FC9 no-data (or context-valid short response if allowed), relevant negative/busy | No-data is legitimate response, not a transport failure; FC11 is not a Class reply |
| Remote DFC=1 | Explicit remote busy state | Suspend normal service until bounded status probe proves ready |
| Short `E5` | Contextually interpreted **only** if service/profile permits and request is unambiguous | Not initial addressed status proof; never synthesize original secondary FC |
| Invalid/echo/wrong address/FC | Out-of-context/malformed evidence | Cannot complete transaction or advance FCB |

The **exact service/function legality table must be checked against the project-authorized IEC standard edition** before coding new protocol rules. A historical `1995/AMD2` citation in old docs does not itself certify a current edition or vendor extension.

## 3. Workstream and milestone execution

### M0 — Reconcile, inventory and write failing fixtures (R00)

1. Fetch latest `master`, open PRs/issues, component `AGENTS`, exact CI and source files; produce conflict/file ownership list before touching code. Reconcile draft PR #136 without discarding accepted evidence or dragging unreleased serial behavior into public RC.
2. Inventory each current rule: fixed/variable/E5/unsupported A2, FC0/1/8/9/11, FCB/FCV, ACD/DFC, Class1/2, GI, commands, status proof, timeout/recovery, A/B. Mark `implemented`, `proposed`, or `not known`.
3. Commit **failing deterministic fixtures** for known deviations (FC1 startup NACK, FC11 after GI/Class, delayed reply, incomplete FT1.2 + E5, ambiguous mutating send and stale owner). No user logs or private captures; synthetic sanitized bytes only.
4. Record benchmark environment, support matrix, profiles, and transaction trace sample. M0 passes only when baseline failures are reproducible, not when a new architecture is merely described.

### M1 — Shared per-link protocol engine and command safety (R01–R04)

1. Build pure `PendingExchange` with identity `ExchangeId, SourceId, LinkEpoch, PrimaryFC, ExpectedSecondary, OriginalTx, FCB/FCV, FrameTxStatus, RetryBudget, firstByteDeadline, frameDeadline, CommandSafetyClass, EvidenceIds`.
2. Move response compatibility/negative/status rules into one testable `Iec101SecondaryResponseValidator` that accepts a complete pending request context. Nothing outside engine may mark a response accepted.
3. Move per-link FCB and safe retransmission to one state-transition function. Retransmit permitted *read/poll* bytes unchanged; forbid blind replay for mutating/uncertain actions. Ensure FC1/NACK and error cases return explicit typed negative verdict.
4. Migrate single-link, then A/B channel to the same contract. Use fixture parity to prevent old dual-link/single-link differences. Preserve underlying public `.ariec`/report models or add compatible adapters rather than breaking replay.
5. M1 gate: **zero false positive acceptances, zero unauthorized FCB transitions, zero automatic ambiguous mutating replay** across shared fixture matrix.

### M2 — Serial transport and FT1.2 streaming lifecycle (R05–R07)

1. One persistent `Ft12StreamReader` per physical port/epoch. Distinguish RX-completed-frame vs raw-byte count, incremental partial fragments, noise/echo, truncated frame and late but well-formed responses without assigning them to the next request without proof.
2. Use monotonic deadlines with distinct **write**, **first-byte**, **inter-character**, **frame-completion** and **recovery** budgets. Minimum serialization floor is `frame_length * bits_per_character * 1000 / baud` with bounded turnaround margin. At 1200 bps, 8E1 = **11 bits/character**; a 250-byte frame takes ~2.29 s on the wire before processing/turnaround.
3. Centralize serial writer lifecycle. Avoid spawning unbounded `Task.Run` per write; a driver-blocked write cannot safely be claimed cancelled/unsent without proof. Record `write accepted by driver` versus `RTU acknowledged` separately.
4. Detect CTS/DSR/DCD/port state, but `Handshake=None + RTS ON` is **not** RTS/CTS flow control; CTS=0 does not itself prove the write was gated. Port unplug/replug creates a **new epoch** and invalidates all pending replies/commands. Automatic reconnection must be explicitly configured and bounded.
5. M2 gate: zero stuck handles/orphan RX workers in repeated reconnect tests; no lost raw byte evidence without explicit loss counters; no unbounded memory/write tasks.

### M3 — Scheduler, GI and A/B controller (R08–R11)

1. Shared policy for `ACD` event drain, `DFC` status-only recovery, Class2 fairness, remote GI and context-safety. Class1 drain bounded by frame and time budgets; Class2 not starved indefinitely.
2. GI is a source-qualified application lifecycle: `GI-TX -> link confirmation -> ACTCON -> objects -> ACTTERM -> coverage`. Support negative, absent and partial independently. Never infer complete GI from link FC0 or one returned value.
3. All command intents use bounded queue and explicit result `RejectedBusy/Stale/NotReady`, `Sent` (not success), `ConfirmedApplication`, `UnknownDelivery`, `PhysicallyObserved` when separately evidenced. For SBO, preserve select/execute/termination and quality rules.
4. A/B controller handles only owner selection, healthy standby, anti-ping-pong, failback, link epoch, and project-specific GI after switch. **Fresh standby proof required**; preserve source identity and stale image marking. No simultaneous active command routing or stale pre-switch control replay.
5. M3 gate: DFC no-spam; ACD drain bounded and fair; no control on standby; no stale failover promotion; no fake GI coverage.

### M4 — Smart, versioned interoperability profiles (R12–R13)

1. Extend existing `Model/Iec10xInteroperabilityProfile.cs` (do **not** create a competing profile authority). Compile `Generic IEC`, historical optional `NUC101 rev2.0`, and **explicit vendor/project overrides** into immutable validated runtime settings. Require edition/source and user authorization for active TX.
2. Capture capabilities and safety gates for link width, CA/COT/IOA widths, unbalanced role, startup reset, GI/clock policies, Class assign, DFC/backoff, serial COM/baud/parity/stop bits/Handshake/RTS/DTR. Serial control-line toggles are operator decisions, never silent trial-and-error.
3. Diagnostics emit `Observed`, `Correlated`, `Inferred`, `Unknown`, cite raw byte span/epoch/pending FC and suggest **non-invasive next test**. Example: FC9 no driver RX bytes -> verify physical COM/adapter rather than changing COT; FC11 after GI -> out-of-context, not standards-violation verdict.
4. M4 gate: incompatible active profiles **fail closed before TX**, passive recording remains possible, no wrong-identity decoding and no misleading conformance badges.

### M5 — Independent interoperability, soak and release proof (R14–R16)

Run [the verification matrix](IEC101_RELIABILITY_VERIFICATION_MATRIX.md) on synthetic frames, independent protocol peer(s), controlled Windows COM adapters and physically authorized RTUs. Use [SICAM A8000 single-link FAT](IEC101_SICAM_A8000_FIELD_FAT.md) and [A/B FAT checklist](IEC101_DUAL_LINK_FAT_CHECKLIST.md), preserving original source/wire metadata.

Especially isolate **1200-bps FIFO/SOE**: 60, 500 and 2,000-event batches; Class1 ACD drain fairness, gap/overrun, RTU queue indications, internal bounded queues, recovery without silent losses. Rate ceiling `1200/11≈109 characters/s` excludes polling, frame overhead and RTU pauses. Acceptance is **integrity with truthful observed loss** under physical limits, not impossible instantaneous retrieval. Record throughput, peak queue, durable drops, retransmits, latency, CPU/memory and host/RTU time accuracy.

M5 **software gate** is all deterministic, independent-peer and exact-head Windows CI passing; **field gate** is additional physical SICAM/other RTU FAT with identical COM/wire/adapter baseline and documented handshake. Do not publish universal IEC101 interoperability or stable-production claims without independently supported physical results.

## 4. Code ownership map (avoid competing implementations)

| Existing owner | Intended refactor / keep |
|---|---|
| `Core/Parsing/Ft12IncrementalFramer.cs` | **Keep canonical byte/frame parser**; expand noise, partial, short-response fixtures |
| `Master/Protocol/Ft12StreamReader.cs` | Persistent reader tied to COM epoch; no per-exchange re-creation |
| `Master/Protocol/Iec101SecondaryResponseValidator.cs` | One pure request-context response legality and typed negative-result authority |
| `Master/Protocol/Iec101UnbalancedBusyGate.cs` (PR #136) | Retain DFC logic behind unified scheduler; never a second connection-health owner |
| `Master/Iec101MasterSession.cs` | Single-link orchestration consuming shared per-link runtime |
| `Master/Iec101/Redundancy/Iec101DualLinkChannel.cs` | Per-channel wrapper around **same** exchange engine |
| `Master/Iec101/Redundancy/Iec101DualLinkRedundancySession.cs` | Role ownership and failover only, no alternative response acceptance path |
| `Master/Transport/SerialByteTransport.cs` | Serialized bounded transport, raw byte telemetry, modem state and lifecycle |
| `Master/Model/Iec10xInteroperabilityProfile.cs` | Existing effective profile authority extended by validated runtime policy |
| `Master.Tests`, Core and desktop acceptance | Shared fixture vectors, fault injection, packet-to-UI/PDF evidence parity |

Do not modify the Passive Monitor TX isolation or rewrite IEC103/IEC104 merely because they use common typed ASDU structures. Any shared-core change must run cross-protocol fixtures.

## 5. Development and CI operating rules

- One accountable P0 workstream: [issue #143](https://github.com/masarray/ARIEC60870/issues/143); PR #136 remains active historical implementation input, **not auto-merge authority**. Group coherent M1–M3 engineering changes in one dedicated implementation branch/PR instead of dozens of conflicting patches.
- Each change must cite `IEC101-Rxx`, a failing/red synthetic fixture or measured real evidence, exact upstream master SHA, design alternative rejected, and negative regression results. Update issue progress at meaningful milestones.
- Use **selective, low-waste CI** during coding: build + focused Master/Core tests + protocol smoke, no repeated queuing of redundant full CI. At the integration gate run one exact-HEAD Windows Release build/format/all xUnit/WPF real screenshots/CodeQL/dependency/hygiene, artifacts + SHA256; inspect test logs and actual UI.
- PR review asks: does this introduce a second owner, false successful response, stale control, silent event loss, increased unbounded memory or changes to public RC without the release gate? If yes, block merge.
- Public `master` and published `v3.6.8-rc.3` stay stable while P0 is validated. A **new test RC** may be cut only after software gates, with explicit physical FAT limitations; a production-ready/reliably interoperable label additionally requires the physical gate.

## 6. Evidence-driven decisions still open

| Decision | Accepted direction | Evidence required before finalizing |
|---|---|---|
| Exact FC acceptance + E5 legality by confirmed service | Use authorized IEC-101 edition + profile, never guess or universalize | Normative clause and positive/negative vector |
| Retry of eligible polling after timeout | Same original frame/FCB, bounded; late response must remain ambiguous if not distinguishable | Duplicate/lost-ACK RTU fixture and FCB evidence |
| Pending command after write timeout | Unknown delivery, **no automatic replay** | Fault injection proving no duplicate control |
| Automatic COM reconnect after unplug | Optional, bounded, new epoch and fresh status proof | Real Windows driver/USB FAT |
| GI after A/B switch | Explicit project policy, not universal NUC default | Approved interoperability list |
| FIFO burst at 1200 bps | Integrity/loss-observability first, throughput measured | A8000 field trace with 60/500/2,000 batch |
| Physical serial RTS/DTR / engineering CTS | Operator-verified wiring only | Same adapter, identical physical COM, IEC_Test comparison |

## 7. Milestone review / handoff template

```text
Milestone / IEC101-Rxx:
Master base / implementation head / pending PRs:
Observed failure (fixture ID or sanitized wire proof):
Component owner and files changed:
State-machine and safety invariant affected:
Before/after trace, typed results, queue metrics:
Focused test, exact-head CI, Windows artifact:
Simulator / independent peer / physical FAT (separate verdicts):
Remaining risk and rollback path:
RC eligibility: NOT YET / Software candidate / Field-validated (with evidence):
Next owner/action:
```

**Completion rule:** all R00–R16 have traceable automated acceptance and verified shared single/A/B behaviors before labeling IEC101 P0 *software complete*. Claim *SICAM-interoperable* only with real supervised FAT; never infer it from simulator or an IEC_Test screenshot from a possibly different COM port.
