# ARIEC60870 Protocol Lab — Free IEC 60870-5-101 / 103 / 104 Tester

[![Build](https://github.com/masarray/ARIEC60870/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/masarray/ARIEC60870/actions/workflows/ci.yml)
[![Pages](https://github.com/masarray/ARIEC60870/actions/workflows/pages.yml/badge.svg?branch=main)](https://github.com/masarray/ARIEC60870/actions/workflows/pages.yml)
[![Package](https://github.com/masarray/ARIEC60870/actions/workflows/release-package.yml/badge.svg)](https://github.com/masarray/ARIEC60870/actions/workflows/release-package.yml)
[![Release](https://img.shields.io/github/v/release/masarray/ARIEC60870?include_prereleases&label=release)](https://github.com/masarray/ARIEC60870/releases)
[![License: Apache-2.0](https://img.shields.io/badge/License-Apache--2.0-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20desktop-0078D4.svg)](#download-and-run)

**ARIEC60870 Protocol Lab** is a **free and open-source** Windows desktop analyzer/tester for IEC 60870-5-103, IEC 60870-5-101, and IEC 60870-5-104 communication checks. It is built for protection relay testing, telecontrol RTU/outstation checks, FAT/SAT evidence, commissioning support, and troubleshooting review.

It can connect to one IEC-103 relay over serial, one IEC-101 outstation over serial, or one IEC-104 server over TCP/IP. The app runs a controlled master/client session, decodes responses, keeps raw TX/RX frame evidence available, and presents the result as readable engineering output for protection, SCADA, panel FAT, site acceptance, and substation automation teams.

No license key. No subscription. No account required. Released under the **Apache-2.0** license.

<p align="center">
  <a href="https://masarray.github.io/ARIEC60870/">
    <img src="docs/assets/screenshots/ariec60870-screen-02.webp" alt="ARIEC60870 line monitor cockpit screenshot" width="92%">
  </a>
</p>

## Why try ARIEC60870?

ARIEC60870 Protocol Lab is designed for engineers who need to answer practical IEC 60870 questions quickly:

- Is the relay/RTU/server answering on the selected COM port or IEC-104 TCP endpoint?
- Does General Interrogation start and finish cleanly?
- For IEC-101/103 serial, are Class 1 events requested only when ACD indicates pending event data?
- For IEC-104, is STARTDT / I-format / S-format / U-format behavior visible in evidence?
- What value/status did the device send, and can the raw frame be reviewed?
- Can the test evidence be exported for FAT/SAT notes, troubleshooting review, or handover?

The tool does not hide the protocol behind a black box. It shows readable evidence first, while keeping raw FUN/INF/Type/COT/DPI/frame details available when deeper investigation is needed.

## Download and run

Get the latest Windows portable package from GitHub Releases:

[Download latest release](https://github.com/masarray/ARIEC60870/releases/latest)

Typical release assets:

```text
ARIEC60870-vX.Y.Z-win-x64-portable.zip
SHA256SUMS.txt
```

First run:

1. Extract the portable ZIP to a local folder.
2. Run `Start-ARIEC60870.bat`.
3. Open **Setup**.
4. Select protocol mode: IEC-103 serial, IEC-101 serial, or IEC-104 TCP/IP. Then set COM/TCP endpoint, address, timeout, GI option, and the protocol interoperability profile: COT size, CA size, IOA size, and IEC-101 link-address length when applicable.
5. Click **Start**.
6. Review **Operator Evidence**, **Value Viewer**, **Relay Event Log**, **Frame Trace**, and **Diagnostics**.
7. Export evidence after the test session.

## Screenshots

| Operator evidence | Setup overlay |
|---|---|
| <img src="docs/assets/screenshots/ariec60870-screen-01.webp" alt="ARIEC60870 operator evidence grid" width="100%"> | <img src="docs/assets/screenshots/ariec60870-screen-05.webp" alt="ARIEC60870 setup overlay" width="100%"> |

| Value and event review | Protocol visibility |
|---|---|
| <img src="docs/assets/screenshots/ariec60870-screen-03.webp" alt="ARIEC60870 value and event review" width="100%"> | <img src="docs/assets/screenshots/ariec60870-screen-04.webp" alt="ARIEC60870 protocol visibility screen" width="100%"> |


## Field-profile improvements in v1.6.2

- IEC-101/103 serial baudrate selection now includes low-rate field channels: 300, 600, **1200**, 2400, 4800, 9600, 19200, 38400, 57600, and 115200 bps.
- The baudrate field is editable, so project-specific serial rates can be entered without rebuilding the app.
- At 1200 bps and below, serial timing is automatically guarded with wider timeout/poll/backoff values to reduce false protocol failures on low-speed channels.
- Default remains 9600 bps for bench convenience, but legacy PLN-style 1200 bps links are now first-class options.

## Forensic-profile improvements in v1.6.0

- Setup now exposes IEC-101/104 interoperability assumptions instead of hiding them: COT length, CA length, IOA length, IEC-101 link-address length, and IEC-104 timer/window fields.
- IEC-101/104 ASDU decoding now supports multiple information objects, including sequence and non-sequence addressing.
- Value Viewer separates engineering value/state from quality flags so engineers do not have to read mixed strings such as `Float=... QDS=...`.
- COT P/N, test flag, originator address, CP56Time2a timestamp, and non-good quality flags are elevated into evidence and findings.
- IEC-104 now raises basic forensic findings for missing STARTDT/TESTFR confirmations and suspicious N(S)/N(R) sequence behaviour.

## Common use cases

- IEC 60870-5-103 relay communication check during panel FAT or bench testing.
- IEC 60870-5-101 serial telecontrol / RTU communication check.
- IEC 60870-5-104 TCP/IP client test for STARTDT, GI, I/S/U APDU visibility, sequence visibility, COT/CA/IOA profile checks, and ASDU object decoding.
- IEC-101/103 master polling verification for Class 1 event data and Class 2 background data.
- Protection relay and SCADA protocol troubleshooting when a device does not respond as expected.
- User-owned IEC-103 signal mapping review using project JSON profiles.
- Evidence export for FAT/SAT notes, troubleshooting records, or engineering handover.

## What you get in the release package

- Windows desktop protocol-aware master/client tester for IEC-103, IEC-101, and IEC-104.
- Dedicated IEC-103 serial, IEC-101 serial, and IEC-104 TCP/IP workspaces.
- Internal demo simulators for IEC-103 relay, IEC-101 outstation, and IEC-104 server workflows.
- CLI tools for active IEC-103 master runs, offline trace analysis, and simulator checks, with IEC-101/104 desktop workflows in the WPF app.
- Sample IEC-103 mapping profile plus PLN PUSERTIF IEC-101/104 seed profile.
- Sanitized IEC-103 plus IEC-101/104 protocol smoke tests.
- Quick Start and Troubleshooting guides.
- Markdown / JSON evidence output.
- License, notices, and checksum file.

## Windows desktop tester

- Protocol-aware selector for IEC-103 serial, IEC-101 serial, and IEC-104 TCP/IP.
- IEC-103 setup only shows serial protection parameters, Class 1/Class 2 policy, and FUN/INF mapping.
- IEC-101 setup only shows serial telecontrol parameters, link/common address, Class 1/Class 2 polling, GI, clock sync, Type ID/COT/IOA evidence fields.
- IEC-104 setup only shows TCP/IP endpoint, common address, STARTDT/APCI runtime information, and Type ID/COT/IOA evidence fields.
- Active master/client session against one relay, outstation, or server.
- Operator Evidence grid for readable session activity.
- Line Monitor / Frame Trace view for raw TX/RX frame inspection.
- Value Viewer snapshot for latest decoded relay points.
- Relay Event Log for relay-timestamped state changes and events.
- AutoTest-style assessment checklist.
- Diagnostics tab for recoverable runtime issues.
- Markdown evidence export.

## IEC-101 / IEC-104 coverage in this build

The added 101/104 mode is intentionally practical and field-test oriented:

- IEC-101 FT1.2 fixed/variable serial frames using controlled Class 1 / Class 2 polling.
- IEC-101 general interrogation `C_IC_NA_1`, optional clock synchronization `C_CS_NA_1`, and common monitoring ASDU decode.
- IEC-104 TCP client handshake with STARTDT, I-format ASDU transfer, S-format acknowledgement, U-format control frames, and TESTFR health check.
- IEC-104 general interrogation over I-format APDU and common ASDU decode for single-point, double-point, measured values, commands, and clock sync.
- Built-in IEC-101 and IEC-104 demo simulators for UI demonstration without field hardware.

## User-owned signal mapping

ARIEC60870 decodes IEC-103 protocol fields such as Type, COT, FUN, INF, DPI/value, timestamp, checksum, and raw frame bytes. For IEC-101/104, the current desktop mode decodes common Type ID, VSQ, COT, common address, IOA, APCI/APDU frame type, and common monitoring/control ASDUs.

Readable project signal names come from your own JSON mapping profile. This avoids guessed vendor naming and keeps FAT/SAT evidence aligned with the approved project signal list.

Example mapping entry:

```json
{
  "schema": "ariec60870-mapping-profile-v1",
  "profileName": "Project A Feeder 01",
  "deviceName": "Relay Bay 01",
  "linkAddress": 1,
  "commonAddress": 1,
  "signals": [
    {
      "id": "bay01.breaker.position",
      "fun": 192,
      "inf": 36,
      "type": "DPI",
      "name": "Breaker Position",
      "group": "Switchgear",
      "stateMap": {
        "1": "Open",
        "2": "Closed"
      }
    }
  ]
}
```

If mapping is loaded, the app can display:

```text
Breaker Position | Closed | FUN 192 / INF 36 | relay timestamp
```

If mapping is not loaded, the app keeps raw protocol evidence visible:

```text
FUN 192 / INF 36 | DPI=2 | relay timestamp
```

## Master polling behavior

ARIEC60870 uses a conservative master policy suitable for relay testing:

```text
Startup:
  Open transport
  Optional startup delay
  Optional reset remote link
  Reset FCB
  Optional clock sync
  Optional General Interrogation
  Bounded GI follow-up

Normal runtime:
  Poll Class 2 at the configured interval

If ACD=1:
  Drain Class 1 until NO DATA / GI END / ACD clear / DFC busy / max drain / timeout

If DFC=1:
  Back off and record busy evidence

If timeout or invalid response:
  Keep FCB state stable, record diagnostic evidence, and recover according to the configured timeout policy
```

Class 1 is treated as pending high-priority/event data, not as a blind continuous polling loop.

## Field validation kit

ARIEC60870 includes a lightweight validation kit so releases are easier to evaluate and regressions are easier to catch:

- dependency-free protocol smoke tests;
- sanitized FT1.2 / ASDU test vectors in `samples/test-vectors/`;
- validation matrix template in `docs/VALIDATION_MATRIX.md`;
- troubleshooting guide for no response, checksum errors, malformed frames, GI issues, and mapping gaps.

Run the protocol checks:

```bash
dotnet run --project tests/ARIEC60870.Protocol.Tests
```

## Evidence privacy

By default, exported evidence uses the mapping profile file name instead of exposing the full local workstation path.

Before sharing reports outside a project team, review project names, relay addresses, serial settings, mapping labels, comments, and raw frame evidence.

## Useful documents

- [Quick Start](docs/QUICK_START.md)
- [Troubleshooting Guide](docs/TROUBLESHOOTING.md)
- [Validation Matrix](docs/VALIDATION_MATRIX.md)
- [Planned Improvements](docs/ROADMAP.md)
- [Test Vectors](samples/test-vectors/README.md)

## Build from source

Requirements:

- .NET 8 SDK
- Windows for the WPF desktop app
- Visual Studio 2022/2026 or command line `dotnet`

Build:

```bash
dotnet restore
dotnet build
```

Run WPF desktop:

```bash
dotnet run --project src/ARIEC60870.Desktop
```

Run a simulated master session without hardware:

```bash
dotnet run --project src/ARIEC60870.Cli -- master --simulate --duration 10 --mapping samples/mapping-profiles/example-user-mapping.profile.json --report out/demo-master-evidence.md --json out/demo-master-evidence.json
```

Run active master against a real relay:

```bash
dotnet run --project src/ARIEC60870.Cli -- master --port COM1 --baud 9600 --link 1 --ca 1 --duration 30 --mapping samples/mapping-profiles/example-user-mapping.profile.json --report out/master-evidence.md --json out/master-evidence.json
```

Run offline analyzer:

```bash
dotnet run --project src/ARIEC60870.Cli -- analyze samples/sample_iec103_trace.log --report out/report.md --json out/report.json
```

Run deterministic slave simulator:

```bash
dotnet run --project src/ARIEC60870.Cli -- slave --port COM2 --baud 9600 --link 1 --ca 1 --duration 300
```

Run protocol smoke tests:

```bash
dotnet run --project tests/ARIEC60870.Protocol.Tests
```

## Product boundary

ARIEC60870 is intentionally focused:

- one IEC-103 connection first;
- active master tester first;
- offline trace analyzer as a supporting mode;
- user mapping profiles instead of guessed vendor profiles;
- raw FUN/INF/Type/COT/DPI/frame evidence always preserved;
- no built-in vendor-specific signal database.

It is not a vendor-specific tester, not a multi-protocol SCADA gateway, and not a replacement for formal site acceptance procedures.

## Release maturity

Current releases are suitable for test-bench evaluation, communication troubleshooting, protocol evidence review, and public feedback.

For production or contractual FAT/SAT use, validate the package with the target relay, project communication settings, and approved project signal mapping before relying on exported evidence.

## License

ARIEC60870 is free and open source under the **Apache License, Version 2.0**. See `LICENSE`.


### v1.6.2 forensic timestamp/link-flag patch

- IEC-101/104 IED/RTU timestamps now propagate into the visible `IED/RTU time` column.
- IEC-101/103 Frame Trace now shows `ACD` and `DFC` columns so Class 1 pending-data and data-flow/busy behaviour are visible without opening raw hex.
- FT1.2 single-character NACK `0xA2`, IEC-101 CP24 time-tags and BCR quality flags are decoded as explicit evidence.


## v1.6.3 persistent setup and forensic audit

The setup window now persists the last field configuration automatically in the user's local AppData folder. Protocol mode, COM/TCP parameters, baudrate, IEC-101/104 interoperability sizes, IEC-104 timer/window profile, polling policy, GI/clock-sync options, mapping profile path, and session duration are restored on next launch.

This release also tightens standards honesty: IEC-101 Balanced mode is shown as planned but not active, link-address-size 0 is recognized as a standard profile case but blocked for the current unbalanced-master workflow, and IEC-104 t0 is included in the visible forensic profile. See `docs/STANDARDS_AUDIT_v1.6.3.md` for the current proof/gap matrix.

## v1.6.4 field forensic fix

- IEC-101 GI follow-up drain now continues until ACTTERM, NO DATA, cancellation, or configured drain limit instead of stopping on the first ACD=0 user-data frame.
- IEC-101/104 SP/DP process objects are always promoted to Value Viewer.
- IEC-101/104 spontaneous digital objects are always promoted to Event Log when COT is spontaneous/remote-command/local-command return information.
- Demo simulators now generate spontaneous SP/DP events so event-log behavior can be tested without hardware.


## v1.6.5 GI/C1/C2 engine activity indicator

- The live activity card now shows `GI/C1/C2` for IEC-101/103 and `GI/I/S` for IEC-104.
- The new GI lamp pulses during General Interrogation activity, including GI command, GI follow-up drain, interrogation COT 20..36, and ACTTERM/GI-end evidence.
- Exported report wording now correctly distinguishes normal Class 1 drain from GI follow-up drain: GI does not stop merely because ACD clears.
- `docs/FORENSIC_AUDIT_v1.6.5.md` records the remaining gaps for IEC-101, IEC-104 and IEC-103 forensic maturity.


## v1.7.0 Command lifecycle and mapping UX proof pass

This build replaces the generic queued control button with explicit command lifecycle actions: Select Open, Operate Open, Select Close, Operate Close; Regulating mode changes the buttons to Lower/Raise; Setpoint mode uses Select/Operate Setpoint. A new Signal List workspace exposes the editable IOA mapping database and command-to-feedback binding rows from the PLN PUSERTIF seed. Operator-facing grids now hide PC arrival time by default and prioritize IED/RTU timestamps to prevent confusing device time with analyzer receive time. Header indicator chips and scrollbar thumb sizing are also stabilized for long sessions.

## v1.6.7 PLN/Pusertif seed and Command Dock

This build adds a user-editable IEC-101/104 IOA point profile system with a bundled `PLN_Pusertif_IEC101_default_seed.json` derived from the uploaded PLN PUSERTIF gateway communication test form. The seed includes real-style CAASDU/IOA/COT defaults, 27 named points, TSS/TSD/TM/RCD/RCA/CTC/SOE/time-sync scenario metadata, and a right-side collapsible Command Dock for GI, clock sync, read, single command, double command, regulating step command, and normalized setpoint command operations while monitoring Value Viewer, Event Log, Frame Trace or Findings.

See `docs/RELEASE_NOTES_v1.6.6.md, docs/RELEASE_NOTES_v1.6.7.md` and `docs/PLN_PUSERTIF_PROFILE_SEED.md`.

## Latest engineering pass

**v1.7.2** adds IEC-101 polling fairness, low-baud scan feasibility diagnostics, a WPF Signal List Editor for IEC-101/104 IOA mapping databases, stronger modern scrollbar styling, and the generic IEC 60870 product icon for the application and landing page.
