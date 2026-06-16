# ARIEC60870 Quick Start

This guide is for the first authorized IEC 60870-5-101 / 103 / 104 communication check using the Windows desktop app.

## 1. Download and open the app

1. Open the latest release page:

   <https://github.com/masarray/ARIEC60870/releases/latest>

2. Download the Windows ZIP asset:

   ```text
   ARIEC60870-vX.Y.Z-win-x64.zip
   ```

3. Extract the ZIP to a local folder.
4. Double-click:

   ```text
   ARIEC60870.exe
   ```

The release package is designed for normal users: one desktop EXE, no start batch file.

## 2. Prepare the connection information

Before starting a session, confirm the approved project/device settings.

| Mode | Information to prepare |
|---|---|
| IEC-104 TCP/IP | Server IP/host, TCP port, common address, ASDU size profile, timeout, GI requirement |
| IEC-101 serial | COM port, baud rate, parity, stop bits, link address, common address, COT/CA/IOA sizes, timeout, GI requirement |
| IEC-103 serial | COM port, baud rate, parity, stop bits, link address, polling/timing settings, optional mapping profile |

Use a test bench, simulator, or authorized project device. Do not connect to a live system without project approval.

## 3. Configure Setup

1. Open **Setup**.
2. Choose **IEC-101 serial**, **IEC-103 serial**, or **IEC-104 TCP/IP**.
3. Enter the approved communication settings.
4. Load a user-owned mapping profile when readable signal names are required.
5. Enable **General Interrogation** when a startup snapshot is needed.
6. Review the settings before pressing **Start**.

## 4. Start the session

Click **Start** and watch these workspaces:

- **Operator Evidence** — readable session activity.
- **Value Viewer** — latest decoded values.
- **Event Log** — decoded state changes and event records.
- **Frame Trace** — TX/RX protocol evidence for deeper review.
- **Diagnostics / Findings** — communication and protocol issues.
- **Report** — evidence scope and direct PDF export.

## 5. First acceptance checks

A healthy first check normally shows:

- the serial port or TCP connection opens without error;
- the device responds after startup communication begins;
- General Interrogation starts and finishes when enabled;
- expected values or events appear in the viewer/log;
- diagnostics do not show persistent timeout, checksum, addressing, or profile mismatch problems;
- unmapped points are still visible using their protocol identifiers.

## 6. Export the PDF report

1. Open **Report**.
2. Click **Refresh** if the preview needs updating.
3. Review the evidence scope.
4. Click **Export PDF**.
5. Choose the output file name and folder.
6. Open the generated PDF and review it before sharing.

The PDF is generated directly by ARIEC60870's built-in native PDF engine. No browser print workflow is required.

## 7. Before sharing evidence

Exported evidence may contain project names, communication settings, mapping labels, and protocol details. Sanitize those details before public issue reports or external sharing.

## 8. Scope reminder

ARIEC60870 is an evidence analyzer/tester for authorized engineering use. It does not replace the approved project FAT/SAT procedure, relay manual, gateway interoperability list, or contractual acceptance criteria.
