# PRD — IEC-101 Virtual Online Sniffer (VOS)

**Date:** 2026-11-06  
**Status:** Proposed, documentation/design only; **NOT IMPLEMENTED** in the current application.  
**Scope:** One Windows PC, an external IEC-101 Master application, an external IEC-101 Slave application, and ARIEC60870 between them. The operator already uses VSPE.  
**Companions:** [ARD](VIRTUAL_ONLINE_SNIFFER_ARD_20261106.md), [VSPE setup and acceptance](VIRTUAL_ONLINE_SNIFFER_VSPE_FAT_20261106.md), [canonical interoperability PRD](IEC10X_INTEROPERABILITY_PRD_20261029.md), [AGENTS](../AGENTS.md).

## 1. Problem / job to be done

An engineer needs to inspect every **observable original octet** in both directions of a local serial IEC-101 test exchange, without two physical USB adapters, without manually replicating frames, and without altering the protocol behavior of either third-party application. The ideal operator workflow is:

**Configure two VSPE virtual pairs -> assign Master and Slave external COM endpoints -> select ARIEC60870 relay-side ports -> Start Relay -> run external applications -> see original HEX, direction, decode, error/loss evidence.**

Existing **IEC-101 Passive Monitor** is explicitly **receive-only**, uses two/four independent RX COM taps, and does **not** forward data. Virtual Online Sniffer is an **active inline transparent byte relay** in a *virtual lab*, not a passive electrical monitor, not a Master, and not an IEC-101 Slave.

## 2. Verified foundation and scope boundaries

- Current master implements a physical/RX-only two/four-port `PassiveMonitorSession`, `WindowsReadOnlySerialTap`, writer-owned passive journal, FT1.2 scanning and WPF evidence pipeline. Do **not** mutate those RX-only transport or safety contracts.
- No virtual inline relay currently exists in master. The open draft PR #95 owns substantial canonical receive/evidence work; new implementation must refresh and reconcile its eventual merge before touching shared Desktop, Monitor or Core sources.
- VSPE **Pair** creates two connected virtual COM ports; using **two** independent pairs allows ARIEC60870 to own one *exclusive* side of each pair. Its **Connector** can be opened twice, but Pair is the clearer default. VSPE supplies the driver; ARIEC60870 must not claim to create kernel COM devices.
- VSPE can allow writes to succeed without delivery when buffer-overrun options are selected. The application must not represent a successful COM write as proof the remote program consumed it.
- Focus V1 on **IEC 60870-5-101 over byte-oriented virtual serial**, one link and two directions. IEC-103 virtual inline and IEC-104 TCP online proxy are separate, future gated extensions; a 101 user should not have to configure unsupported protocols.
- No automatic project-specific Master/Slave simulator, no active polling/GI/command insertion, no vendor-specific interpretation or bypass, no physical serial production tap, no hardware signal-level/bit-edge/timing guarantees.

## 3. User stories

**US-01** Integration tester: configure third-party IEC Master and Slave on one PC, launch the virtual inline session, observe both directions in a single dense chronological Trace with exact HEX.

**US-02** Protocol analyst: see unknown, invalid, incomplete, E5 and other unrecognized octets immediately, even if interpretation fails; inspect exact source, offset, timing quality and checksum evidence.

**US-03** Test engineer: prove whether request bytes arrived at ARIEC60870 and were submitted to the opposite COM, independently of whether the other program actually accepted/acted on them.

**US-04** FAT reviewer: export the original observation and relay-health manifest with explicit unknown/loss intervals; reopen an archive without VSPE installed.

**US-05** Operator: safely Stop and restart without deadlocked COM handles, duplicated directions, stale frames, false green connection status or hidden reconnect.

## 4. Primary topology — two VSPE Pair devices

    IEC Master (COM11) <-> [VSPE Pair A] <-> COM12 [ARIEC VOS] COM13 <-> [VSPE Pair B] <-> (COM14) IEC Slave

Port assignments are examples, not hardcoded defaults. The relay opens only COM12 and COM13; Master owns COM11, Slave owns COM14.

**Byte flow:** A->B: COM11 write -> COM12 receive -> relay writes COM13 -> COM14 receive. B->A: COM14 write -> COM13 receive -> relay writes COM12 -> COM11 receive.

Every direction has a single source and a single destination. ARIEC60870 must **never** open the same COM port as either external app in the Pair topology. A two-Connector alternative may be documented later after tested compatibility; Splitter/Bridge chains are not the primary V1 dependency.

## 5. Functional requirements and acceptance

| ID | Priority | Requirement | Acceptance |
|---|---|---|---|
| VOS-01 | P0 | Distinct UI mode `IEC-101 Virtual Online Sniffer`, explicitly labeled **Inline relay (transmitting)**, isolated from RX-only Passive Monitor | Switching modes does not reconfigure or issue Write on passive tap; clear inline warning |
| VOS-02 | P0 | Two distinct operator-selected relay-side COMs, validated before opening (available, not duplicated, 8 data bits, baud/parity/stop bits, no unsupported handshake) | Invalid/occupied port never shows Ready; error names exact port and action |
| VOS-03 | P0 | Binary-transparent, independent two-direction forwarding, **no frame-gated forwarding** | For each direction the bytes at receiving peer equal sent bytes, including arbitrary binary, broken IEC frame, `00`, `E5`, `10`, `68`, `A2`; arbitrary chunk boundaries |
| VOS-04 | P0 | Preservation of **order within each direction**, with separate direction IDs and one writer per egress | Concurrent traffic does not merge direction buffers; no echo loops, injection, duplication or endian conversion |
| VOS-05 | P0 | Immediate original-byte observation independent of valid frame decoding | Unknown/noise/invalid/checksum-failed/fragmented bytes visible as original HEX or explicitly indexed raw-span records, not silently absent |
| VOS-06 | P0 | Canonical FT1.2 interpreter and existing typed evidence/Trace/Values/Events reused; no second parser/WPF protocol truth | Same fixture decodes with existing canonical Core scanner; invalid ASDU never becomes trusted Value/Event |
| VOS-07 | P0 | Explicit ingress, egress-attempt and write-completed counters/evidence; distinguish OS write completed vs remote app consumed | Fault/disconnect does not report delivered to peer; byte totals, in-flight bytes and destination faults diagnosable |
| VOS-08 | P0 | Bounded allocation/queues; relay unaffected by UI rendering/decoder slowness; no silent relay byte drops | Under saturation, explicit fault/partial state and safe stop rather than silent discard or infinite backlog |
| VOS-09 | P0 | Deterministic Start/Stop/restart and epoch isolation, close both handles on partial startup failure | Repeated 100 start/stop cycles: no leaked handles/stale frames; Stop never strands disabled buttons |
| VOS-10 | P1 | Compact dual-direction Trace, stable AUTO/HOLD, exact raw offsets, copy HEX, independent diagnostic counters | HOLD/filter changes never stop forwarding or rewrite capture; unknown frame selectable |
| VOS-11 | P1 | Opt-in full raw journal; per-direction streams, optional ingress/egress progress metadata, integrity and gap records; offline replay | Export byte-identical for complete recordings; power-loss/disk-fault capture explicitly Partial, no invented completeness |
| VOS-12 | P1 | VSPE Pair bench instructions, diagnostics for COM ownership and virtual-peer not-yet-observed; save local operator config | A new operator can wire four endpoints without touching registry or creating COM drivers in the app |
| VOS-13 | P1 | Measured performance and integrity benchmark, recorded host/OS/VSPE settings | Quantified relay p50/p95 latency, CPU/memory/GC, zero observed byte mismatch in healthy documented test |
| VOS-14 | P2 | Future virtual IEC-103, IEC-104 TCP proxy via separately approved transport adapters | Do not advertise or expose until verified; preserve per-protocol state and evidence scopes |

### Semantics: no misleading guarantees

1. **Read bytes**: bytes returned from a virtual COM at an ARIEC60870 ingress port.
2. **Write attempted**: bytes submitted to an outbound COM write.
3. **Write completed**: serial driver reported success for the attempted write, **not** proof of peer read, IEC acknowledgment, RTU event persistence or physical-wire transit.
4. **Observed response**: bytes received back from the other external application, not confirmation of a real field device.
5. **Decoded frame**: valid FT1.2 interpretation, not a prerequisite for forwarding.
6. **Incomplete/unknown**: missing, failed, timed out, malformed, bounded buffer overflow, driver error or unknown remote state must not be translated to success.

### Data-fidelity acceptance

- Exact concatenation of ingress chunks must equal the original generated byte stream, per direction, under a healthy VSPE bench; exact concatenation of peer-received bytes must equal the generated stream. Compare binaries / SHA-256, not only count of decoded frames.
- Build test fixtures for `E5`, `10 ... 16`, `68 L L 68 ... CS 16`, malformed length/checksum, truncated frames, arbitrary garbage, back-to-back frames and edge bytes `00/FF/A2`.
- Distinguish a well-formed IEC101 single-character response from raw unexpected bytes; never invent a direction from unknown protocols or silently drop them.
- Master/Slave shall be allowed to use 1200 bps, 8E1 and other valid 8-bit UART profiles; any VSPE baud-emulation behavior is an **external driver option** to test, not an inferred wire-speed guarantee.

## 6. UX: compact, transparent, operator-first

Setup row: **Mode | Master-side relay COM | Slave-side relay COM | UART | Start Relay / Stop**. A concise topology help opens an explicit four-port mapping, with two pairs labeled A/B. Keep existing WPF design tokens, dense single Trace grid, select-to-inspect byte spans; no oversized cards or separate analyzer window.

Visible running states: `Idle`, `Opening COMs`, `Relay Ready (COMs open, peer unverified)`, `Traffic observed`, `Degraded capture`, `Stopping`, `Faulted`. Distinguish ingress bytes, write completed bytes, faults and decoder status per direction; no green 'link healthy' inferred from handle-open alone. Button `Start Relay`, not misleading `Start Passive Monitor`. One Help/Setup action should explain VSPE pairs; user can configure ports manually and app can detect names, but must not silently install/reconfigure VSPE.

The raw monitor must stay useful with **zero valid IEC frames**. Show ingress byte and write counters and actionable diagnostics (`No ingress bytes on COM12; check Pair A and external Master`) rather than an empty viewport.

## 7. Safety, compatibility and non-goals

- Inline mode is **not RX-only**. On Start, it actively writes bytes that arrived from one source onto the other virtual link; show this clearly and restrict V1 to user-owned/lab virtual endpoints. No production tapping, no claim of electrically nonintrusive behavior.
- No RTS/CTS/DSR/DTR/DCD/Break/RS-485 direction transparency in V1; `Handshake.None` is the supported baseline. If an external program requires hardware flow control, reject as unsupported until a separately proven design exists.
- No protocol-byte rewriting, automatic CRC repair, unsolicited GI/polls, fake E5, reconnect-induced replay, line-idle emulation, changing serial parameters of other applications, or silent fallback to Master mode.
- No dedicated VSPE binary, kernel driver or proprietary API embedding; user supplies independently licensed VSPE. No GPL driver source may be copied into this Apache-2.0 repository.
- Malformed/unknown frames are **forwarded unchanged** (unless transport fault interrupts forwarding), while decoder may classify them as invalid. This is core to the feature.

## 8. Verification stages and delivery ownership

| Gate | Outcome required | Proof source |
|---|---|---|
| G0 / design | PRD, ARD, VSPE bench + risk model reviewed; impact on draft #95 reconciled | Documentation review, no implementation claim |
| G1 / core relay | Transport interface + two-direction, loss-observable relay tested without WPF/real COM | Deterministic in-memory fake COMs, malformed/random 2k/5k/50k byte streams, backpressure/partial writes |
| G2 / evidence | Shared Core FT1.2, raw spans and no-decode/no-drop observation, opt-in recording | Golden cross-mode parity + negative integrity tests |
| G3 / WPF | Setup, preflight, Start/Stop and dense Trace integration, no active polling | Windows WPF smoke, exact-handled faults, source/epoch verification |
| G4 / VSPE | Real two Pair driver Windows test with 3 applications; both directions proven | Screenshots, four COM ownership, per-direction SHA-256, restart/traffic test, latency measurements |
| G5 / release | Validated Windows portable candidate with exact SHA and documented limitations | CI + manual bench results; do not claim field/physical FAT |

**Performance targets, not achieved facts:** healthy VSPE benchmark zero application-observed mismatched bytes; bounded memory independent of stream duration; measure ingress-to-write-complete latency p50/p95/p99 under known workloads and UI load, and capture overflow/driver faults. Do not invent universal latency SLA because driver/baud emulation/Windows scheduling differ.

## 9. Implementation priorities

**One meaningful engineering implementation milestone after design:** `VOS-01/02/03/04/07/08/09` together, a standalone relay engine with deterministic tests and optional test-harness CLI; then integrate shared observation/capture and WPF (`VOS-05/06/10/11/12`) without rewriting #95 or touching active IEC101 Master TX. The PR in which these documents are added is **docs-only**; none of these acceptance items should be marked Implemented merely because this PR merges.

## 10. Independently consulted public references (behavioral basis only)

- VSPE device overview: https://eterlogic.com/help/vspe/index.html
- VSPE Pair properties: https://eterlogic.com/help/vspe/DevicePairPage.html
- VSPE Connector properties: https://eterlogic.com/help/vspe/DeviceConnectorPage.html
- VSPE Splitter buffering behavior (risk comparison): https://eterlogic.com/help/vspe/DeviceSplitterPage.html
- VSPE Bridge modem-register limitation: https://eterlogic.com/help/vspe/DeviceBridgePage.html
- Microsoft SerialPort.DataReceived contract: https://learn.microsoft.com/en-us/dotnet/api/system.io.ports.serialport.datareceived
- Microsoft SerialPort class/raw byte API: https://learn.microsoft.com/en-us/dotnet/api/system.io.ports.serialport
- Microsoft Channels and bounded backpressure: https://learn.microsoft.com/en-us/dotnet/core/extensions/channels
- Virtual serial pair reference (alternative, not a dependency): https://com0com.sourceforge.net/

References describe external product/API behavior, not permission to copy driver, SDK, commercial code or UI. IEC101 decoding remains governed by ARIEC60870's canonical Core and the project-approved interoperability profile.
