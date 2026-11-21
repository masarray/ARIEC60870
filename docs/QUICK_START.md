# ArSee Quick Start — v3.6.20 Public Stable

This guide covers a first **authorized** IEC 60870-5-101/103/104 test with ArSee on Windows x64. The public web walkthrough is at [Quick Start](https://masarray.github.io/arsee/quick-start.html).

## 1. Install or extract the official release

Use **[ArSee v3.6.20 Public Stable](https://github.com/masarray/arsee/releases/tag/v3.6.20)**: [Windows installer](https://github.com/masarray/arsee/releases/download/v3.6.20/ArSee-v3.6.20-setup-win-x64.exe) or [portable ZIP](https://github.com/masarray/arsee/releases/download/v3.6.20/ArSee-v3.6.20-win-x64.zip). Compare the file with the [official SHA-256 manifest](https://github.com/masarray/arsee/releases/download/v3.6.20/SHA256SUMS.txt).

For a regular installation, run the official setup EXE. For portable use, fully extract the ZIP into a separate folder and run `ArSee.exe`; the source ZIP is not a Windows package. To upgrade a registered legacy installation under `Programs/ARIEC60870`, close the IEC session and run the official installer directly. **Do not uninstall manually or delete** `%LOCALAPPDATA%\ARIEC60870` user data.

## 2. Prepare approved connection details

| Mode | Project-approved parameters |
|---|---|
| IEC-101 active unbalanced single or A/B serial | COM port(s), baud rate, parity, serial control lines, link address, CA, COT/CA/IOA widths, timeout, startup reset/GI policy |
| IEC-101 passive RX-only | Dedicated receive interface, approved serial framing and capture scope; strictly **no active TX** |
| IEC-103 serial | COM port, serial framing, link address, polling/timing and optional user mapping |
| IEC-104 TCP | Host/IP, TCP port, common address, ASDU widths, timeout and optional startup GI |

Use an isolated bench, simulator, or project-authorized RTU/IED. Do not change line controls, reset or command live equipment without authorization.

## 3. Open Connection Setup and Connect

1. Open **Setup**, select the protocol mode and enter verified parameters.
2. Optionally load a user-owned IOA/Signal List mapping to provide names and calibrated engineering values.
3. In active IEC-101/104, review **General interrogation on connect** and reset/clock settings. A deliberately saved OFF is respected.
4. Click **Connect** or press **Enter** immediately after the Setup popup appears, even before interacting with a dropdown.
5. When the connection starts successfully Setup closes. **Disconnect** stops the current session; if Setup is open, it remains visible. Enter does not disconnect an active session.

## 4. Inspect source-qualified evidence

- **Raw Frames:** original TX/RX and decoded selected-frame details.
- **Values:** current decoded points, quality, and calibrated Signal Mapping values.
- **Events:** retained event history; Include/Exclude/search continues to filter new sessions from the first event.
- **GI:** identify original TI100/COT6 TX, matching ACTCON, GI data and ACTTERM as distinct milestones, not just a healthy serial link.
- **Redundancy:** IEC-101 A/B active/standby ownership, link health, and the detailed selected timeline.
- **Controls / Report:** review authorized command evidence and the PDF report before export.

IEC-101/104 signed scaled TI11/12/35 measurements display a numeric **INT** (without the decoder label `Scaled=`); an operator-configured Signal Mapping calibration takes precedence, while original raw evidence is preserved.

## 5. Export and protect evidence

Use **Report** to review the evidence and export the native PDF. Check known endpoint behavior, addressing, signal identity, GI/command milestones, errors and any incomplete evidence. Remove station names, sensitive addresses and customer-specific mappings before public sharing.

**Limit:** ArSee is a free Apache-2.0 evidence analyzer/tester for approved engineering workflows—not production SCADA or an all-vendor IEC conformance/physical FAT certificate.
