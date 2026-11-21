# ArSee · IEC 60870 Protocol Tester & Analyzer

**Understand the exchange. Find the issue. Keep the evidence.**

ArSee is a **free, open-source Windows engineering tool** for **IEC 60870-5-101, IEC 60870-5-103 and IEC 60870-5-104**. Inspect original TX/RX frames alongside decoded values, events, diagnostics and evidence-linked findings, then prepare a native PDF report for engineering review.

[![CI](https://github.com/masarray/arsee/actions/workflows/ci.yml/badge.svg?branch=master)](https://github.com/masarray/arsee/actions/workflows/ci.yml)
[![Website](https://github.com/masarray/arsee/actions/workflows/pages.yml/badge.svg?branch=master)](https://masarray.github.io/arsee/)
[![Apache 2.0](https://img.shields.io/badge/license-Apache--2.0-23748a)](LICENSE)
[![Windows x64](https://img.shields.io/badge/platform-Windows%20x64-64748b)](https://github.com/masarray/arsee/releases/latest)

**[Product website](https://masarray.github.io/arsee/)** · **[Download](https://masarray.github.io/arsee/download.html)** · **[Watch the demo](https://masarray.github.io/arsee/demo.html)** · **[Quick Start](https://masarray.github.io/arsee/quick-start.html)** · **[Field Wiki](https://masarray.github.io/arsee/wiki.html)**

<p align="center"><a href="https://masarray.github.io/arsee/"><img src="site/assets/screenshots/ariec60870-evidence-workspace.webp" width="90%" alt="ArSee IEC 60870 engineering evidence workspace" /></a></p>

## Download for Windows x64

**Latest public stable: [v3.6.20](https://github.com/masarray/arsee/releases/tag/v3.6.20)** · Free to use · No subscription or activation key.

| Package | Official download |
| --- | --- |
| **Installer** — per-user Windows setup | [ArSee v3.6.20 Setup EXE](https://github.com/masarray/arsee/releases/download/v3.6.20/ArSee-v3.6.20-setup-win-x64.exe) |
| **Portable** — extract and run | [ArSee v3.6.20 ZIP](https://github.com/masarray/arsee/releases/download/v3.6.20/ArSee-v3.6.20-win-x64.zip) |
| **Integrity & release notes** | [SHA-256 checksums](https://github.com/masarray/arsee/releases/download/v3.6.20/SHA256SUMS.txt) · [Versioned release](https://github.com/masarray/arsee/releases/tag/v3.6.20) |

Official release packages may be unsigned. Use the [download guide](https://masarray.github.io/arsee/download.html) for setup, portable use and upgrade guidance.

## From a connection symptom to engineering evidence

When a serial or TCP session looks connected but SCADA values are missing, the next question is **what actually happened on the wire?** ArSee connects the original protocol frames with their interpretation so engineers can move from symptoms to supporting observations.

| Workflow | What you can inspect |
| --- | --- |
| **Connect and observe** | IEC-101 active unbalanced single/A-B serial, IEC-103 serial and IEC-104 TCP, plus separate receive-only IEC-101 monitoring |
| **Inspect the exchange** | Original TX/RX telegrams, selected-frame interpretation, protocol fields, addresses, errors and diagnostics |
| **Understand the signals** | Value snapshots, events, project-specific signal mapping and engineering-unit scaling |
| **Investigate issues** | Smart Findings, GI and command-feedback evidence where supported, with source-frame references |
| **Document the work** | Evidence report preview and native PDF export |

The original communication remains inspectable even when decoding is incomplete or a signal has no project mapping.

## Practical workflow

1. **Configure** IEC-101/103 serial or IEC-104 TCP using authorized project parameters; passive and offline workflows are available where appropriate.
2. **Connect or capture** and observe what ArSee received or transmitted in Frame Trace.
3. **Investigate** the relevant Values, Events, GI/Controls and Smart Findings; follow references back to original evidence.
4. **Document** the session with the built-in PDF report.

**[Explore screenshots and the demo](https://masarray.github.io/arsee/demo.html)** · [Step-by-step Quick Start](https://masarray.github.io/arsee/quick-start.html) · [Full User Guide](docs/USER_GUIDE.md)

## Capabilities

- **IEC-101:** unbalanced master sessions, Class 1/2 polling, GI, source-linked control feedback, dual-link A/B workflow and a separate receive-only serial monitor.
- **IEC-103:** protection-relay exchanges, supported FUN/INF fields, values and event interpretation.
- **IEC-104:** TCP connection, APCI/ASDU interpretation, STARTDT/TESTFR and supported transactions.
- **Signal List:** user-owned signal mapping and Factor or X0/X100/Y0/Y100 two-point scaling of applicable analog values.
- **Evidence workspace:** Frame Trace, Values, Events, Diagnostics, Smart Findings, selected-frame inspection and native PDF reporting.

See the [release notes](https://github.com/masarray/arsee/releases/tag/v3.6.20) for behavior delivered by the current stable build. Illustrative screenshots and demo media are not protocol conformance evidence.

## Open source and traceable releases

ArSee is published under **Apache-2.0** with a public source tree, contribution and security policies, automated CI and versioned Windows releases. Release packages include published SHA-256 hashes and software supply-chain metadata.

**[Source code](https://github.com/masarray/arsee/tree/master/src)** · **[CI](https://github.com/masarray/arsee/actions/workflows/ci.yml)** · **[Windows package workflow](https://github.com/masarray/arsee/actions/runs/44898156357)** · **[Security](SECURITY.md)** · **[Changelog](CHANGELOG.md)**

These controls support transparency and traceability of the software build. They do not imply accredited IEC conformance or universal physical RTU interoperability.

## Compatibility and scope

**Upgrading legacy installations:** New executables and packages are named **ArSee**, while legacy user data paths remain compatible. For v3.6.17/v3.6.18 installed under `Programs/ARIEC60870`, close active IEC/capture sessions and run the official v3.6.20 installer directly. Preserve `%LOCALAPPDATA%/ARIEC60870` user data and mappings. [Migration guidance](docs/RELEASE_INSTALL_FOLDER_MIGRATION_v3.6.19.md).

ArSee is an engineering tester/analyzer, **not a production SCADA master or an accredited conformance test suite**. Use active operations only on authorized systems. A protocol acknowledgment does not by itself prove physical control operation. IEC-101 balanced mode and universal multi-vendor compatibility are not advertised.

## Learn and contribute

[Field Wiki](https://masarray.github.io/arsee/wiki.html) · [Quick Start](docs/QUICK_START.md) · [Troubleshooting](docs/TROUBLESHOOTING.md) · [Architecture](docs/ARCHITECTURE.md) · [Roadmap](docs/ROADMAP.md) · [Release history](CHANGELOG.md)

[Contributing](CONTRIBUTING.md) · [Issues](https://github.com/masarray/arsee/issues) · [Support](SUPPORT.md) · [Code of Conduct](CODE_OF_CONDUCT.md) · [License](LICENSE) · [Third-party notices](THIRD_PARTY_NOTICES.md)

Remove sensitive customer identifiers, credentials, topology and private captures before submitting public reports.
