# SICAM A8000 ↔ ARIEC60870 IEC-101 — Field FAT/SAT

Read-only engineering checklist for a reproducible diagnosis. Do not disturb a
live SCADA link or issue control/clock synchronization during initial tests.

## Preflight

1. Isolate the RTU's serial port from any production SCADA master.
2. Record the actual SICAM CPU/communication module model, port, firmware,
   RS232/RS485 electrical interface and wiring diagram.
3. Verify SICAM IEC-101 controlled/secondary **unbalanced** mode. ARIEC60870
   currently implements unbalanced master; balanced IEC-101 is not supported.
4. Match baud rate, parity, data bits and stop bits with the RTU. Confirm
   Link Address and its 1-/2-byte size independently of ASDU Common Address.
5. Begin with one COM port, `Handshake=None, RTS=ON, DTR=OFF` (existing
   defaults). Only enable RTS/CTS or DTR when hardware cabling requires it.
6. Disable GI and commands initially; prove basic link status/Class 2 RX first.

## Interpret ProtocolTrace

For Link Address 105 with two address bytes, master status request:
`10 49 69 00 B2 16`; a valid secondary status response with no ACD/DFC:
`10 0B 69 00 74 16`. Other protocol-valid secondary replies are possible.

If RX repeats `10 49 69 00 B2 16`, it is PRM=1 (primary), not evidence of an
RTU response. That may indicate local echo, another master, reversed wiring,
or an inappropriate RTU link mode. The trace alone cannot isolate the
physical source.

Inspect Diagnostics codes `IEC101-RX-TX-ECHO`,
`IEC101-RX-DELAYED-TX-ECHO`, `IEC101-RX-FOREIGN-PRIMARY`,
`IEC101-RX-UNEXPECTED-PRIMARY`, `IEC101-RX-LINK-ADDRESS-MISMATCH`
and `IEC101-RX-INTEGRITY`. Copy the full row, not just the warning caption.

Read `IEC60870-SERIAL-OPEN-PROFILE` for the configured serial parameters,
RTS and DTR, and local CTS/DSR/DCD input states. CTS/DSR/DCD are *physical
adapter input signals*, not application-layer acknowledgements.

## Isolation matrix

| Step | Safe action | Expected proof |
| --- | --- | --- |
| A | Disconnect RTU from test COM on an isolated bench | No genuine secondary RX |
| B | Connect only Link A and confirm unbalanced role | RX PRM=0, correct LA |
| C | Compare link address length 1 vs 2 bytes | No address rejection |
| D | Compare None vs hardware RTS/CTS only if wired | No hangs or spurious link success |
| E | Keep test COM connected and start Class 2 | Secondary RX responds consistently |
| F | Enable GI after link validates | Data, ACTCON and ACTTERM when supported |
| G | Connect Link B independently | Standby supervision without Class 1/2 |
| H | Controlled active-path interruption in FAT | Failover only to validated standby |
| I | Stop/restart the session | Port closes and reopens without stale UI state |

If a primary echo persists with the physical RTU line disconnected, test
USB/RS232 adapter loopback, cross-channel splicing and COM-port routing.
Document TX/RX LEDs, null-modem orientation and required physical jumper
connections from the exact Siemens module and converter manuals; RJ45/DB9
pinouts are not universal.

## Export checklist

Preserve the full `.ariec` or ProtocolTrace and report, both COM names,
per-link address, CA, baud/framing, output settings, CTS/DSR/DCD status,
time of tests, firmware and wiring photo/diagram. Compare full rejected
frame counts, validated secondary responses, GI completion and failover
journal before and after each single-variable correction. No on-hardware
interoperability claim is made by simulator/unit tests alone.

## Post-P0/P1 field finding: COM open and GI are not proof of RTU data

The application now makes three distinct claims:

- **COM open:** Windows successfully opened the serial adapter. No evidence yet of a remote station.
- **IEC-101 secondary link verified:** A checksum/length-valid secondary reply (PRM=0) arrived on the expected link address.
- **GI verified:** A successful GI exchange followed by actual process ASDUs/ACTTERM. A TX GI frame alone does not prove this.

For unverified serial links, the engine issues bounded **Request Link Status**
probes and does **not** flood the outstation with GI or Class 1 traffic.
Startup GI proceeds when a genuine secondary reply is seen. A silent GI
or failed Class 1 response stops its drain instead of sending dozens of
requests while Value Viewer stays empty.

When troubleshooting a *real* SICAM:
1. Export a **full** trace rather than selected rows.
2. Check if `IEC101-LINK-UNVERIFIED` is present, then inspect `IEC101-RX-*`.
3. Check for `IEC101-GI-UNCONFIRMED` when link proof succeeded but GI was
   not positively acknowledged.
4. If GI responds but no process values appear, inspect CA/COT/IOA profile
   sizes, raw ASDUs and the actual GI data/ACTTERM evidence.
5. Verify with the module's documented RS232 pinout and an isolated test
   port, without disrupting a production SCADA channel.

A simulated positive secondary test is necessary for software regression,
but cannot certify the actual cable, SICAM module role, or GI configuration.

## IEC-101 serial startup and slow telegrams (2026-11-10)

The configured `StartupDelayMs` now applies after opening the COM port(s)
for both single and dual unbalanced Master modes, before any FT1.2 TX.
At 1200 bps the maximum FT1.2 telegram can require several seconds on
the wire, so the reader allows one bounded baud-derived completion window
after a real `10`/`68` start byte; fixed and variable telegrams have
separate wire-time caps so a truncated short reply cannot stall for the
maximum variable-frame duration. **Silence retains `ResponseTimeoutMs`**;
invalid checksum, wrong secondary address/PRM, echo and cancellation are
still rejected or handled as before. This corrects software timing gaps;
it is not proof that physical SICAM communication has passed FAT.

For a COM-open but unverified-link report, preserve raw TX/RX and the exact
`IEC101-RX-*` diagnostics. If RX is empty, isolate the cable, RTS/DTR/flow
control, baud/framing and the converter. If RX repeats TX, inspect loopback
or other-master/port routing. Do not bypass secondary proof, automatically
issue station reset or alter production wiring to force a green connection.

## Serial driver write timeouts and status proof (2026-11-10)

`IEC60870-TRANSPORT-WRITE-TIMEOUT` is a local OS/driver write failure, not
proof that the remote RTU received the frame. The baud-derived driver write
budget is independent of the protocol response deadline; diagnostics capture
COM, attempted TX bytes, elapsed time, limit, CTS/DSR/DCD, configured RTS/DTR
handshake and pending TX bytes. With RTS/CTS flow control, CTS=0 may block TX;
with Handshake=None CTS=0 alone is not proof. Compare a known-good IEC_Test
profile on an isolated bench before changing production cabling. A bare E5
cannot prove FC9 Request Link Status; the Master rejects it with
`IEC101-RX-UNEXPECTED-E5-STATUS` instead of falsely starting GI. E5 remains
valid contextually for command ACK and Class 1/2 NO DATA.

## Context-aware unbalanced link response validation (2026-11-10)

Active IEC-101 now validates the pending **primary function code** against each addressed
secondary reply, following IEC 60870-5-101 Amendment 2, subclause 6.2.1.1 / Table 10:

- FC0/FC1/FC3/FC7 (confirmed services) -> secondary FC0 positive or FC1 NACK;
  short E5 may confirm success when context permits.
- FC8/FC9 status request -> secondary FC11 only; DFC=1 is observed busy,
  **not** verified healthy status. FC0, FC8, FC9, E5 and a link-service error
  cannot substitute for a positive status confirmation.
- FC10/FC11 class polling -> secondary FC8 user data, FC9 no data or
  context-permitted E5 (inferred no-data, without inventing ACD/DFC bits).
- A2 is preserved raw as an unsupported IEC-101 byte, never promoted to a
  legitimate IEC-101 NACK/ACK or link health. FC14/FC15 link-service errors
  remain visible diagnostics, not successful replies.

The same validator serves active single and A/B unbalanced channels. This is
**not** balanced-mode implementation, NUC certification, serial hardware proof
or a change to production RTS/CTS, COM-port policy or auto-reset behavior.
IEC101/103/104 mode-specific interoperability must still be validated against
real connected equipment and a controlled project-selected profile.
