# ArSee v3.6.13 — Reliable IEC-101 Master and verifiable IEC-104 GI (Windows x64)

This document describes the **v3.6.13 release candidate source contents**, prepared 16 November 2026. It does not assert that v3.6.13 is already published or that any later binary passes acceptance. The latest published stable remains v3.6.12 until a successful tagged release completes.

## IEC-101 single-link and redundant A/B Master

The reviewed standalone hardening PRs [#219](https://github.com/masarray/ARIEC60870/pull/219)–[#225](https://github.com/masarray/ARIEC60870/pull/225) now use one bounded transaction and FCB/FCV authority per physical IEC-101 COM link, correlate observed secondary responses with the active request, handle delayed ambiguous FC11 responses, preserve ACD Class1/Class2 fairness, and recover real serial-port faults with independent A/B ownership and controlled backoff. No application-level mutating control is automatically replayed after a transport ambiguity.

The next stages [#226](https://github.com/masarray/ARIEC60870/pull/226), [#227](https://github.com/masarray/ARIEC60870/pull/227) and [#228](https://github.com/masarray/ARIEC60870/pull/228) harden SBO selection/execute source and command epochs, feedback-target identity, and the IEC-104 TCP connection association. GI is counted at application level only for a request-correlated positive ACTCON and matching ACTTERM on the same physical owner/association. **A GI ACTTERM does not prove all configured IOAs were received. A command ACTCON or ACTTERM does not prove physical breaker operation.**

The broad historic draft [#145](https://github.com/masarray/ARIEC60870/pull/145) remains unmerged; its entire branch was **not** rolled into this release.

## Mapping, SOE and bounded memory

[#229](https://github.com/masarray/ARIEC60870/pull/229) makes read-only names consistent across online Values and re-opened portable `.ariec` captures, with a source-scoped CA-ambiguity check before allowing unique process IOA display aliases. The bundled mapping for **FEEDER CB STATUS = CA105 / IOA16712689 / TI31** is available when the operator loads an appropriate Signal List. If two stations or other IOA identities conflict, the original CA/IOA/TI remains visible and the name is **not guessed**. This never changes command addresses, GI expected-point proof or original wire bytes.

Accepted IEC-101 spontaneous digital **objects** have their own cumulative counter. 60/500/2,000-event deterministic synthetic tests validate software accounting and bounded report retention, not physical RTU-generated events. Both single/A/B IEC-101 and IEC-104 now use the shared O(1) bounded evidence FIFO, with exact in-memory eviction counters and unchanged live event delivery.

[#230](https://github.com/masarray/ARIEC60870/pull/230) additionally prevents IEC-104 falsely terminating the GI receive burst or incrementing its GI counter from wrong-source/CA/OA/QOI, negative/TEST or pre-ACTCON responses. The lifecycle is evaluated only for accepted IEC-104 I-frames after an actual successful GI TX on the current connection, using the same tracker implementation as IEC-101.

## Software acceptance and release delivery

**Before publication**, confirm the **actual v3.6.13 tag/commit** passes Windows Release (warnings-as-errors), all six xUnit suites and 19 protocol smoke tests, native real-WPF OBS-C 1,400-frame acceptance, CodeQL, repository hygiene, release-package executable check, official Windows installer/portable ZIP structure, SHA-256 checksums, SPDX SBOM and build provenance. A green PR is not a green release artifact.

**The stable v3.6.13 Package workflow will refuse publication without a successful exact-commit native Windows independent updater FAT** including genuine installed-old-release update-worker acceptance. This safeguard must not be bypassed. The real installed updater requires operator approval; the portable ZIP does not update itself. Windows builds remain unsigned.

**FIELD FAT PENDING:** No claim of complete 60/500/2,000 IEC-101 SOE acquisition at 1200 bps from an actual RTU/gateway until independent IEC_Test comparison on the same cable/adapter and original RX/TX evidence proves parity; device FIFO overflow, physical USB COM recovery, A/B failover, real command feedback, multiple IEC-103 devices and multi-vendor IEC-104 TCP peers remain independently unqualified. The application is an engineering tester/analyzer, not a certified production SCADA master or IEC conformance test suite.

The website/README links deliberately continue to point at published **v3.6.12** until v3.6.13 is actually published, then the public landing, download guide and checksums must be synchronized to the immutable new release assets.
