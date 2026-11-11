# SICAM A8000 ↔ ARIEC60870 IEC-101 — Field FAT/SAT

> **P0 completion and evidence gate:** See [IEC-101 Reliability Implementation Plan](IEC101_RELIABILITY_IMPLEMENTATION_PLAN.md) and [Verification Matrix](IEC101_RELIABILITY_VERIFICATION_MATRIX.md) (particularly P01–P08) for the strict distinctions between driver RX bytes, complete frames, correlated services, genuine GI and physical FAT. Track work in [issue #143](https://github.com/masarray/ARIEC60870/issues/143); PR #136 is **not included** in public rc.3. A physical comparison must use the **same physical adapter/wiring/port**, not merely an identical COM number.

## 2026-11-11 field finding — IEC-101 link-reset handshake resolves startup failure

**Operator-reported physical test:** On a SICAM A8000 IEC-101 connection,
enabling **Reset remote link on connect** resolved the previously observed
connection/GI-startup problem. This is a genuine **user field observation**,
not a captured/signed full FAT: hardware firmware, raw frame export and
independent 1200-bps SOE batch evidence were not provided in this update.

**Startup recommendation (active unbalanced IEC-101 only):**
- In new ArSee IEC-101 single-link and A/B setups, the FC0 **link-layer**
  reset option is enabled by default. This synchronizes the secondary link
  state; it is **not** IEC-101 FC1 *reset user process* and not the
  application C_RP_NA_1 command.
- Keep the checkbox editable. A saved explicit OFF remains OFF across
  upgrades; do not silently override an operator's existing setup.
- IEC-103/104 defaults remain unchanged. IEC-101 Passive Monitor remains
  receive-only (0 TX); never send FC0 on passive capture.
- FC0 must be issued only by the authorized active Master on an isolated
  or approved IEC-101 link, not by a second master on a live production
  SCADA channel. Resetting the link can discard a pending link-layer
  message; it does not preserve the previous confirmed FCB epoch.
- **Do not infer** complete GI ACTCON/ACTTERM, deterministic FCB recovery
  after a driver unplug, 500/2000 SOE/FIFO integrity or multi-vendor
  interoperability solely from this successful connection.

For reproducible evidence record startup TX FC0 and subsequent RTU link
ACK/status, then actual GI/process Class 1/2 RX, port/adapter, baud and
link/CA widths. See the separate [umbrella IEC-101 reliability issue #143](https://github.com/masarray/ARIEC60870/issues/143)
for unfinished M1–M5 milestones.

---

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
