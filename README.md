# ArSee · Open-Source IEC 60870-5-101/103/104 Protocol Tester & Analyzer

[![CI](https://github.com/masarray/ARIEC60870/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/masarray/ARIEC60870/actions/workflows/ci.yml)
[![Pages](https://github.com/masarray/ARIEC60870/actions/workflows/pages.yml/badge.svg?branch=master)](https://github.com/masarray/ARIEC60870/actions/workflows/pages.yml)
[![Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)
[![Windows x64](https://img.shields.io/badge/platform-Windows%20x64-blue)](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.10)

**ArSee** is the public product name. Existing source namespaces, executable names and published asset filenames retain `ARIEC60870` for compatibility. A coordinated rename to `masarray/arsee` is planned; the existing Pages URL stays canonical until cutover.

**From raw IEC 60870 traffic to inspectable engineering evidence.**

ArSee is a free, Apache-2.0 open-source Windows protocol tester and evidence analyzer for **IEC 60870-5-101, IEC 60870-5-103, and IEC 60870-5-104**. Inspect original TX/RX frames, supported protocol interpretations, Values, Events, diagnostics, and native PDF evidence for authorized FAT/SAT, commissioning support, research and troubleshooting.

**[Product website](https://masarray.github.io/ARIEC60870/)** · **[Download guide](https://masarray.github.io/ARIEC60870/download.html)** · **[Quick Start](https://masarray.github.io/ARIEC60870/quick-start.html)** · **[Field Wiki](https://masarray.github.io/ARIEC60870/wiki.html)**

## Download for Windows x64

| Release channel | Version | Official downloads | Purpose |
|---|---|---|---|
| **Latest public stable** | **v3.6.10** | **[Windows installer](https://github.com/masarray/ARIEC60870/releases/download/v3.6.10/ARIEC60870-v3.6.10-setup-win-x64.exe)** · [Portable ZIP](https://github.com/masarray/ARIEC60870/releases/download/v3.6.10/ARIEC60870-v3.6.10-win-x64.zip) | Event → Original Frame forensic evidence, 31 bay-aware IEC-101/104 signal labels, bounded 1200-bps FT1.2 reception and tested Windows updater |
| **Previous stable** | **v3.6.9** | [Stable release page](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.9) | Introduced operator-approved installed Windows self-updater |

**ArSee** is the application branding; compatibility installer/ZIP filenames retain ARIEC60870.

**v3.6.10 is the latest stable software release, not physical IEC/SCADA conformance certification.** Verify the [SHA-256 checksum](https://github.com/masarray/ARIEC60870/releases/download/v3.6.10/SHA256SUMS.txt) before running currently unsigned binaries. The [release page](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.10) includes SPDX SBOM; [exact-source Windows package workflow](https://github.com/masarray/ARIEC60870/actions/runs/43436696449) records full tests, WPF acceptance and provenance.

> GitHub's [`/releases/latest`](https://github.com/masarray/ARIEC60870/releases/latest) resolves to the newest non-prerelease publication. The version-pinned links above point to the complete public assets. Draft engine PR #145 is not included.

<p align="center">
  <a href="https://masarray.github.io/ARIEC60870/">
    <img src="site/assets/screenshots/ariec60870-evidence-workspace.webp" alt="Illustrative ArSee protocol evidence workspace" width="90%" />
  </a>
</p>

*Illustrative product media. The [v3.6.10 package workflow](https://github.com/masarray/ARIEC60870/actions/runs/43436696449) validates the release executable and real-WPF evidence; physical RTU FAT remains separate.*

## Supported engineering workflows

| Area | What you can inspect |
|---|---|
| **IEC-101 active serial (unbalanced)** | Serial link and Class 1/2 polling, GI and selected command protocol evidence |
| **IEC-101 dual-link A/B** | Independent active/standby link states and redundancy trial diagnostics, **not certified production redundancy** |
| **IEC-101 passive** | Receive-only evidence interpretation from configured capture connections; electrical isolation depends on hardware |
| **IEC-103 active serial** | Relay protocol exchanges and supported event/value interpretation |
| **IEC-104 TCP/IP** | TCP session, APCI and ASDU data, STARTDT/TESTFR and transaction evidence |
| **Engineer workstation** | Raw Frames, Values, Events, GI/Controls where available, selected-frame inspector and contextual Smart Findings |
| **Reporting** | Native PDF evidence export with source frame references |

## What's improved in stable v3.6.10

- **Event Journal → Original Frame:** context-aware live-follow, historical paging and navigation to captured TX/RX frames. Missing original TX is reported as incomplete evidence rather than invented.
- **Bay-aware SCADA labels:** the **bundled 31-point utility I/O example** distinguishes FEEDER, KOPEL, TRAFO CB/Local-Remote, relevant commands, busbar voltage and RTU alarms. Invalid CA/IOA/IEC Type ID families fail closed; existing user-owned profiles are **not automatically overwritten**.
- **IEC-101 at 1,200 bps:** bounded baud-aware FT1.2 frame completion after a physical start, persistent RX framing across polls per COM epoch, and regression tests for incomplete telegrams and ambiguous E5. This does **not** prove physical RTU interoperability.
- **Windows upgrade assurance:** operator-approved updates for the **installed** edition (portable ZIP does not self-replace), qualified by native Windows updater FAT, 1,032 passing xUnit tests and real-WPF acceptance on the [exact source commit](https://github.com/masarray/ARIEC60870/actions/runs/43435781986).

Read [v3.6.10 release notes](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.10) and [package / provenance results](https://github.com/masarray/ARIEC60870/actions/runs/43436696449). The [shared IEC-101 transaction core draft #145](https://github.com/masarray/ARIEC60870/pull/145) remains **unmerged**. **FIELD FAT PENDING:** physical SICAM A8000 1,200-bps SOE 60/500/2,000 batches, GI/SBO lifecycle, COM recovery and A/B failover.

An acknowledged frame or GI termination is **not** proof that every configured point was received or that a physical control operated.

## Five-minute first run

1. [Download the v3.6.10 installer](https://github.com/masarray/ARIEC60870/releases/download/v3.6.10/ARIEC60870-v3.6.10-setup-win-x64.exe) or [portable ZIP](https://github.com/masarray/ARIEC60870/releases/download/v3.6.10/ARIEC60870-v3.6.10-win-x64.zip) and verify [checksums](https://github.com/masarray/ARIEC60870/releases/download/v3.6.10/SHA256SUMS.txt); fully extract the ZIP, then launch `ARIEC60870.exe` directly (do not run it inside the compressed archive).
2. In **Setup**, choose IEC-101 serial, IEC-101 passive, IEC-103 serial or IEC-104 TCP and enter the project-approved COM/TCP, serial handshake, link and ASDU address settings.
3. Connect only to an **authorized simulator or isolated test endpoint**. Do not guess RTS/DTR states or reset a live outstation.
4. Compare original TX/RX frame evidence with the interpretation in Raw Frames, Values, Events and GI/Controls as available.
5. Export native PDF evidence, then sanitize topology, project and customer identifiers before sharing.

Further documentation: [User Guide](docs/USER_GUIDE.md) · [Quick Start](docs/QUICK_START.md) · [Testing Strategy](docs/TESTING_STRATEGY.md) · [IEC-101 A/B field checklist](docs/IEC101_DUAL_LINK_FAT_CHECKLIST.md) · [Release notes](docs/RELEASE_NOTES.md).

## Scope and safety boundaries

- **Full IEC-101 SICAM A8000 physical interoperability is not certified.** [Draft PR #145](https://github.com/masarray/ARIEC60870/pull/145) shared transaction/FCB and dual-link hardening is **excluded** from v3.6.10; COM recovery, GI/SBO evidence and 1,200-bps SOE FIFO still need physical FAT. Simulated CI is not a wired RTU pass.
- IEC-101 **balanced mode**, universal multi-vendor interoperability, industrial FIFO/SOE endurance, and electrically isolated 2/4-port capture are not certified.
- **HOLD** pins the visible trace viewport while acquisition continues; it does not itself guarantee durable archival recording.
- ArSee is **not** a production SCADA master, gateway, safety device or accredited IEC conformance test suite. Use active functions on approved isolated benches only.

## Build, inspect and contribute

The WPF UI, IEC protocol components and typed evidence engine live under `src/`; six test projects and site hygiene checks live in `tests/`. The English/Indonesian public landing site is maintained in `site/`, with a generated fallback mirror in `docs/`.

The [CI workflow](https://github.com/masarray/ARIEC60870/actions/workflows/ci.yml) covers Release build, protocol regression and Windows WPF acceptance. The [Package workflow](https://github.com/masarray/ARIEC60870/actions/workflows/release-package.yml) verifies the *actual published executable* and builds ZIP, installer, checksums, SBOM and provenance. These are software checks, **not physical FAT**.

Read [Contributing](CONTRIBUTING.md), [Security](SECURITY.md), [Support](SUPPORT.md), [Code of Conduct](CODE_OF_CONDUCT.md), [Changelog](CHANGELOG.md), [License](LICENSE) and [Third-party notices](THIRD_PARTY_NOTICES.md). Apache-2.0 permits commercial use under its terms; ARIEC60870 requires no subscription or activation key.

For issues, [send a reproducible report](https://github.com/masarray/ARIEC60870/issues) with sanitized TX/RX HEX, version, OS, protocol profile and steps. Never publish sensitive captures, credentials or private substation topology.
