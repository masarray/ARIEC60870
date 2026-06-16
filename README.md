# ARIEC60870 Evidence Analyzer

[![CI](https://github.com/masarray/ARIEC60870/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/masarray/ARIEC60870/actions/workflows/ci.yml)
[![Pages](https://github.com/masarray/ARIEC60870/actions/workflows/pages.yml/badge.svg?branch=master)](https://github.com/masarray/ARIEC60870/actions/workflows/pages.yml)
[![Package](https://github.com/masarray/ARIEC60870/actions/workflows/release-package.yml/badge.svg)](https://github.com/masarray/ARIEC60870/actions/workflows/release-package.yml)
[![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20desktop-0078D4.svg)](#download-and-run)

**ARIEC60870 Evidence Analyzer** is a free Apache-2.0 Windows desktop application for authorized IEC 60870-5-101, IEC 60870-5-103, and IEC 60870-5-104 evidence review, FAT/SAT preparation, commissioning checks, and troubleshooting records.

The app helps engineers run a focused communication session, review decoded evidence, inspect protocol details, and export a professional PDF report. It is intended for authorized engineering environments only.

Latest release: [download the Windows ZIP](https://github.com/masarray/ARIEC60870/releases/latest).

<p align="center">
  <a href="https://masarray.github.io/ARIEC60870/">
    <img src="site/assets/screenshots/ariec60870-screen-02.webp" alt="ARIEC60870 Windows protocol evidence analyzer" width="92%">
  </a>
</p>

## What it is for

Use ARIEC60870 when you need to:

- check a single IEC-101, IEC-103, or IEC-104 endpoint in a controlled test environment;
- confirm startup communication and General Interrogation behavior;
- review decoded values, events, diagnostics, and protocol evidence;
- use project-owned mapping profiles for readable signal names;
- export a professional PDF evidence report for review, FAT/SAT notes, or handover.

ARIEC60870 is not a production SCADA system, not a gateway, not a redundant master station, and not a replacement for an approved project test procedure.

## Download and run

1. Open the latest release page:

   [Download latest release](https://github.com/masarray/ARIEC60870/releases/latest)

2. Download this Windows asset:

   ```text
   ARIEC60870-vX.Y.Z-win-x64.zip
   ```

3. Extract the ZIP to a local folder.
4. Double-click:

   ```text
   ARIEC60870.exe
   ```

The user release is intentionally simple: one self-contained desktop EXE in the package, with no start batch file required.

Release integrity files are also published:

```text
ARIEC60870-vX.Y.Z-sbom.spdx.json
SHA256SUMS.txt
```

## First use

1. Open **Setup**.
2. Select the protocol mode: **IEC-101 serial**, **IEC-103 serial**, or **IEC-104 TCP/IP**.
3. Enter the project-approved communication settings for the test device.
4. Enable **General Interrogation** when a startup snapshot is needed.
5. Load a mapping profile if readable signal names are required.
6. Click **Start**.
7. Review **Operator Evidence**, **Value Viewer**, **Event Log**, **Frame Trace**, **Diagnostics**, and **Report**.
8. Open **Report** and click **Export PDF** when the evidence is ready.

For a fuller walkthrough, read the [User Guide](docs/USER_GUIDE.md) and [Quick Start](docs/QUICK_START.md).

## Connection overview

### IEC-104 TCP/IP

Use **IEC-104 TCP/IP** for authorized endpoint checks over a TCP connection. Enter the server address, TCP port, common address, and ASDU profile values from the device or project interoperability document.

### IEC-101 serial

Use **IEC-101 serial** for serial RTU or gateway checks. Select the COM port and enter the approved serial settings, link address, common address, and ASDU size profile.

### IEC-103 serial

Use **IEC-103 serial** for protection relay communication checks. Select the COM port and enter the approved serial settings, link address, polling/timing values, and optional mapping profile.

## Export a PDF report

1. Run a session or open a saved capture.
2. Open **Report**.
3. Click **Refresh** if the preview is not current.
4. Click **Export PDF**.
5. Choose the output file name and folder.
6. Review the generated PDF before sharing it.

The PDF is generated directly by the built-in native PDF engine. No external PDF conversion workflow is required.

## Screenshots

| Operator evidence | Setup overlay |
|---|---|
| <img src="site/assets/screenshots/ariec60870-screen-01.webp" alt="ARIEC60870 operator evidence grid" width="100%"> | <img src="site/assets/screenshots/ariec60870-screen-05.webp" alt="ARIEC60870 setup overlay" width="100%"> |

| Value and event review | Protocol visibility |
|---|---|
| <img src="site/assets/screenshots/ariec60870-screen-03.webp" alt="ARIEC60870 value and event review" width="100%"> | <img src="site/assets/screenshots/ariec60870-screen-04.webp" alt="ARIEC60870 protocol visibility screen" width="100%"> |

## Core capabilities

- IEC 60870-5-101 serial evidence workflow.
- IEC 60870-5-103 serial relay evidence workflow.
- IEC 60870-5-104 TCP/IP evidence workflow.
- Startup communication, General Interrogation, value, event, diagnostic, and frame review.
- User-owned JSON mapping profiles for readable project signal names.
- Professional PDF evidence report generated by the built-in native PDF engine.
- Sanitized protocol smoke tests and xUnit regression suites.

## Included in the user release package

- `ARIEC60870.exe` — the Windows desktop application.
- `README_RELEASE.txt` — short first-run instructions.
- `docs/` — User Guide, Quick Start, Troubleshooting, Validation Matrix, and Release Packaging notes.
- `samples/` and `profiles/` — neutral example files.
- `LICENSE`, `NOTICE`, `THIRD_PARTY_NOTICES.md`, `CHANGELOG.md`.

## Build from source

Requirements:

- .NET 8 SDK
- Windows for the WPF desktop app
- Visual Studio 2022 or newer, or command-line `dotnet`

Build:

```bash
dotnet restore ARIEC60870.sln
dotnet build ARIEC60870.sln --configuration Release
```

Run desktop:

```bash
dotnet run --project src/ARIEC60870.Desktop
```

Run tests:

```bash
dotnet test ARIEC60870.sln --configuration Release
```

Create the user release package locally:

```powershell
pwsh ./scripts/publish-windows-portable.ps1 -Version 3.6.5
```

## Documentation

- [User Guide](docs/USER_GUIDE.md)
- [Quick Start](docs/QUICK_START.md)
- [Troubleshooting Guide](docs/TROUBLESHOOTING.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Native PDF Engine](docs/NATIVE_PDF_ENGINE.md)
- [Validation Matrix](docs/VALIDATION_MATRIX.md)
- [Testing Strategy](docs/TESTING_STRATEGY.md)
- [Release Packaging](docs/RELEASE_PACKAGING.md)
- [Roadmap](docs/ROADMAP.md)
- [Changelog](CHANGELOG.md)
- [Test Suite](tests/README.md)

## Security and privacy

Protocol traces and exported reports may contain project names, station labels, communication addresses, mapping labels, and evidence details. Review exported files before sharing them outside the project team.

Please report security issues using [SECURITY.md](SECURITY.md).
