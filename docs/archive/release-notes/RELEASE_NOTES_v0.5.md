# ARIEC60870 v0.5 - WPF Master Tester Foundation

## Purpose

v0.5 introduces the first Windows desktop product shell for ARIEC60870.

The product direction remains locked:

> ARIEC60870 is an Apache-2.0 IEC 60870-5-103 Master Tester that connects to protection relays acting as IEC-103 slaves, with deep evidence and controlled polling behavior.

## Added

- New `ARIEC60870.Desktop` WPF project.
- COM setup panel for single connection master testing.
- Serial configuration:
  - COM port
  - baudrate
  - 8E1 / 8N1 / 8O1
  - link address
  - common address
  - timeout
  - duration
  - Class 2 polling interval
  - max Class 1 drain frames
- Startup options:
  - Reset Remote Link
  - Reset FCB
  - Clock Sync
  - General Interrogation
  - Class 2 after startup
- Start / Stop controls.
- Live evidence grid bound to `Iec103MasterEvidenceEvent`.
- Findings grid bound to `Iec103MasterFinding`.
- KPI cards:
  - TX / RX
  - Class 1 / Class 2
  - NO DATA
  - DPI events
  - findings
- Selected row inspector:
  - decoded detail
  - polling reason
  - ACD/DFC
  - ASDU/COT
  - raw hex
- Desktop Markdown evidence export.
- `RUN_DESKTOP.bat`.

## Preserved From v0.4

- Active master state machine.
- Class 2 normal/background polling.
- Class 1 only when ACD=1 or bounded GI follow-up.
- Stop conditions for Class 1 drain: NO DATA, GI END, ACD clear, DFC busy, drain limit.
- Timeout evidence and controlled recovery.
- Clean-room Apache-2.0 protocol implementation.

## Not Added Yet

- Vendor-specific project FUN/INF mapping editor.
- Rich PDF report.
- Advanced GI lifecycle visual panel.
- WPF decode tree for ASDU fields.
- Slave/IED simulator.
- Dual redundancy.

## Next Recommended Version

v0.6 should focus on vendor-neutral IEC-103 semantic mapping:

- FUN/INF profile JSON.
- DPI state naming.
- relay event category.
- GI sequence panel.
- improved report formatting.
