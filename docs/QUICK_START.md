# ARIEC60870 Quick Start

This guide is for a first IEC 60870-5-103 / 101 / 104 communication check using the Windows desktop app.

## 1. Prepare the connection

- Use a known working USB-to-serial or RS-485 adapter.
- For IEC-103/101 serial, confirm the device communication port, baudrate, parity, stop bit, link address, and common address.
- For IEC-104 TCP/IP, confirm the server IP address, TCP port, common address, and that the server accepts a client connection.
- Keep the first test simple: one master/client and one relay, RTU/outstation, or IEC-104 server.

## 2. Start the desktop app

For a portable release package, run:

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
| COM Port | Adapter COM port shown in Windows Device Manager for IEC-103/101 |
| IEC-104 TCP | Server IP/host and TCP port, normally 2404 |
| Baudrate | Device configured value. Low-rate IEC-101 field channels such as 1200 bps are supported; common bench values include 9600/19200 bps |
| Parity | Device configured value for serial |
| Link Address | IEC-103/101 link address of the relay/outstation |
| Common Address | Device common address used by the station/project |
| Timeout | Start conservative, then tune later |
| Reset FCB | Enabled for normal startup |
| General Interrogation | Enabled when a startup snapshot is needed |

## 4. Start the session

Click **Start** and watch these areas:

- **Operator Evidence** — readable session activity.
- **Line Monitor / Frame Trace** — raw TX/RX evidence.
- **Value Viewer** — latest decoded values.
- **Relay Event Log** — relay timestamped events.
- **Diagnostics** — communication and protocol issues.

## 5. First acceptance checks

A healthy first check normally shows:

- serial port opens without error;
- device answers after link reset, STARTDT, or first request;
- General Interrogation starts and finishes, when enabled;
- IEC-101/103 Class 2 polling continues at the configured interval;
- IEC-101/103 Class 1 is requested only when event data is pending;
- IEC-104 I/S/U frame activity is visible after STARTDT;
- timeout/checksum/malformed counters stay low or zero.

## 6. Export evidence

Export Markdown evidence after the test. Review the report before sharing it outside the project team.

ARIEC60870 intentionally avoids exposing full local mapping profile paths in exported public evidence by default.


### v1.6.2 forensic timestamp/link-flag patch

- IEC-101/104 IED/RTU timestamps now propagate into the visible `IED/RTU time` column.
- IEC-101/103 Frame Trace now shows `ACD` and `DFC` columns so Class 1 pending-data and data-flow/busy behaviour are visible without opening raw hex.
- FT1.2 single-character NACK `0xA2`, IEC-101 CP24 time-tags and BCR quality flags are decoded as explicit evidence.

## Persistent setup

From v1.6.3 onward, the setup window saves the last configuration automatically. Set the protocol, COM/TCP parameters, baudrate, COT/CA/IOA sizes, IEC-104 timers/window, and polling options once; the next launch restores them.

Saved preferences are local to the workstation and are stored under the user's LocalAppData `ARIEC60870` folder. If a USB serial converter is unplugged, the remembered COM port is still shown so the engineer can reconnect the same converter without retyping the whole profile.

## IEC-101 profile honesty

The current IEC-101 engine implements unbalanced master polling. Balanced mode and link-address-size 0 are recognized as important standard/profile cases, but they are not enabled for validation in this build to avoid false proof claims.
