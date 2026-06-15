# ARIEC60870 Quick Start

This guide is for a first IEC 60870-5-101 / 103 / 104 communication check using the Windows desktop app.

## 1. Prepare the connection

- For IEC-101/103 serial, confirm the communication port, baudrate, parity, stop bit, link address, and common address.
- For IEC-104 TCP/IP, confirm the server IP address, TCP port, common address, and that the server accepts a client connection.
- Keep the first test simple: one master/client and one relay, RTU/outstation, gateway, or IEC-104 server.
- Use sanitized mapping profiles for public testing and project-approved mapping profiles for real FAT/SAT work.

## 2. Start the desktop app

For a release package, run:

```bat
Start-ARIEC60870.bat
```

For source development:

```bash
dotnet run --project src/ARIEC60870.Desktop
```

## 3. Open Setup

Set these values first:

| Field | Recommended first value |
|---|---|
| Protocol | IEC-103 serial, IEC-101 serial, or IEC-104 TCP/IP |
| COM Port | Adapter COM port shown in Windows Device Manager for IEC-101/103 |
| IEC-104 TCP | Server IP/host and TCP port, normally 2404 |
| Baudrate | Device configured value. Low-rate IEC-101 channels such as 1200 bps are supported; common bench values include 9600/19200 bps |
| Parity | Device configured value for serial |
| Link Address | IEC-101/103 link address |
| Common Address | Device common address used by the station/project |
| COT/CA/IOA size | Match the device interoperability profile |
| Timeout | Start conservative, then tune later |
| Reset FCB | Enabled for normal IEC-101/103 startup |
| General Interrogation | Enabled when a startup snapshot is needed |

## 4. Start the session

Click **Start** and watch these areas:

- **Operator Evidence** — readable session activity.
- **Frame Trace** — raw TX/RX evidence and protocol field visibility.
- **Value Viewer** — latest decoded values.
- **Event Log** — decoded state changes and event records.
- **Diagnostics / Findings** — communication and protocol issues.
- **Report Preview** — standalone HTML evidence report preview and export.

## 5. First acceptance checks

A healthy first check normally shows:

- serial port or TCP socket opens without error;
- device answers after link reset, STARTDT, or first request;
- General Interrogation starts and finishes, when enabled;
- IEC-101/103 Class 2 polling continues at the configured interval;
- IEC-101/103 Class 1 is requested when event data is pending;
- IEC-104 I/S/U frame activity is visible after STARTDT;
- timeout/checksum/malformed counters stay low or zero;
- decoded values/events match the approved mapping profile or remain visible as raw protocol addresses when unmapped.

## 6. Export evidence

Use **Report Preview** or selected evidence export after the test. Review the report before sharing it outside the project team.

Exported evidence may contain project names, communication settings, mapping labels, and raw protocol frames. Sanitize those details before public issue reports or external sharing.

## 7. Scope reminder

ARIEC60870 is an evidence analyzer/tester. It does not replace the approved project FAT/SAT procedure, relay manual, gateway interoperability list, or contractual acceptance criteria.
