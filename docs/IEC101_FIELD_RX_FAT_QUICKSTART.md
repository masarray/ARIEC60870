# IEC-101 RX-only Field FAT — portable two/four COM toolkit

**Purpose:** make the existing independent passive serial acquisition engine usable from an extracted Windows x64 ZIP, alongside the GUI. This tool does **not** replace the GUI, does **not** implement an active IEC-101 master, and does **not** certify a serial installation. It never sends IEC-101 commands.

## Wiring and test order

1. Start on an **isolated bench**, with production SCADA disconnected or an approved electrically isolated/high-impedance tee installed. Connect monitor **RX + reference GND only**, in accordance with the actual converter electrical design. **Do not connect monitoring adapter TXD, RTS, DTR** to the plant. Opening even a software RX-only serial port can toggle modem control lines, so software alone is not physical non-interference protection.
2. Use **two separate COM ports** for one full-duplex serial link: Link A SCADA TX (master to RTU) and Link A RTU TX (RTU to master). For dual A/B links use **four independent RX adapters/ports**, two per link; do not bridge the circuits together.
3. Match actual UART baud, parity, 8 data bits and stop bits. Capture is byte-oriented; wrong UART settings can corrupt bytes before decoding. Choose IEC LA/COT/CA/IOA widths from the actual interoperability settings; these affect interpretation, not raw journal contents.
4. Download the distinct **ARIEC60870-IEC101-RX-Field-Tools-win-x64** Actions artifact, extract fully, open PowerShell or Command Prompt in the extracted folder, and run the commands below. This is a **field engineering artifact**, separate from the public single-EXE GUI release.
5. Begin with a brief, bounded **1200 bps** or site-profile capture with full recording and look for both physical directions. Repeat with 4 COMs only after both circuits are safely tapped. Never start with a blind control/GI command on a production system.

## Quick commands (PowerShell)

```powershell
.\ARIEC60870.Monitor.Cli.exe list-ports

.\ARIEC60870.Monitor.Cli.exe preflight --scada-com COM7 --rtu-com COM8 --output C:\ARIECField --baud 1200 --parity Even --stop-bits Two --seconds 180 --link-width 2 --cot-width 1 --ca-width 1 --ioa-width 2

.\ARIEC60870.Monitor.Cli.exe capture --scada-com COM7 --rtu-com COM8 --output C:\ARIECField --baud 1200 --parity Even --stop-bits Two --seconds 180 --link-width 2 --cot-width 1 --ca-width 1 --ioa-width 2

# Link B: add BOTH options to the same preflight/capture command:
# --link-b-scada-com COM9 --link-b-rtu-com COM10

.\ARIEC60870.Monitor.Cli.exe verify --folder C:\ARIECField\ARIEC-PASSIVE-YYYYMMDD-HHmmss-fff-xxxxxxxx
```

Use the site profile rather than copying these example address sizes. `preflight` **does not open a COM port**. It validates the 2/4 distinct port selection and framing; checks only the Windows COM inventory and prints source-to-port mapping. It cannot prove a port isn't already busy or verify electrical isolation. `capture` preflights again and refuses to start if a selected COM is not enumerated. Press Ctrl+C for a graceful session stop, journal finalization and post-capture integrity verification.

## Interpretation of results

The capture creates the canonical `manifest.json`, independent `stream-00.bin`, `stream-01.bin` (and `02/03` for Link B), byte-level checksums, derived frame and transaction evidence, plus **`field-summary.json`**. The latter summarizes per-direction counts and SHA verification; it contains no source raw bytes. The raw binary files and manifest remain authoritative. Always keep their contents together when moving the capture folder.

| Exit code | Meaning |
| --- | --- |
| 0 | Preflight lists all selected COMs, **or** capture journal verified with some physical bytes observed; protocol correctness not implied |
| 1 | Invalid parameters / unhandled I/O or environment error |
| 2 | Journal incomplete, writer/driver uncertainty or failed SHA/CRC verification |
| 3 | One or more configured COMs missing at preflight, **or** all streams recorded zero data |

A completely quiet direction during a short capture is a **warning**, not proof that the cable is faulty. Likewise, SHA/CRC can prove application journal consistency but cannot prove a USB adapter did not drop bytes before delivery. USB latency, physical serial timing and RTU IEC behavior require independent bench measurements.

## Minimum acceptance on real adapters

- One isolated link, two COMs: valid source mapping, recognizable original FT1.2 bytes, no physically connected monitor outputs, Start/Stop port reopen, no data loss claimed beyond what checksums prove.
- Two isolated links, four COMs: A/B source separation, both directions on both links, no cross-link inferred commands, 2/4-port connect/disconnect test, verified journal hashes.
- Mixed scenarios: quiet RTU, wrong baud/parity, one disconnected adapter, wrong link address widths, unexpected PRM=1 echo and malformed/checksum-failed frame; retain full evidence and correct uncertainty.
- A sustained 1200-bps, multi-hour soak remains **operator FAT**, not something a CI runner or simulated transport can certify.

Upload only sanitized evidence after checking station addresses, source labels, filenames and timing for confidentiality. Use GUI Trace/Values/Events/Controls/GI to inspect decoded meanings; this CLI is deliberately a headless raw-capture/verification companion.
