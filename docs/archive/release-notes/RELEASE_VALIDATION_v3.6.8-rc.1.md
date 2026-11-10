# ARIEC60870 v3.6.8-rc.1 — Public RC validation evidence

**Status:** Published public prerelease, not stable or certified.  
**Publication:** 2026-11-10 03:26:46 UTC.  
**Source/tag SHA:** `a6ea99858db85a6b8c3bd1d59c309d771243e031`.  
**Build/publish provenance:** [Package #42749227002](https://github.com/masarray/ARIEC60870/actions/runs/42749227002).  
**Release:** [v3.6.8-rc.1](https://github.com/masarray/ARIEC60870/releases/tag/v3.6.8-rc.1).

This document records **observed automation evidence**, not claims about untested physical devices.

## Exact source and gate results

The release tag and public release target resolve to the same source commit. The Package workflow `resolve`, Windows `package`, and `release` jobs completed successfully; the package job compiled both user deliverables from the same checkout.

| Test project / gate | Observed result |
| --- | ---: |
| Core xUnit | 93 passed, 0 failed, 0 skipped |
| Repository xUnit | 23 passed, 0 failed, 0 skipped |
| Reporting xUnit | 5 passed, 0 failed, 0 skipped |
| Desktop xUnit | 396 passed, 0 failed, 0 skipped |
| Monitor xUnit | 129 passed, 0 failed, 0 skipped |
| Master xUnit | 206 passed, 0 failed, 0 skipped |
| **Total xUnit** | **852 passed, 0 failed, 0 skipped** |
| Dependency-free protocol smoke | **19/19 passed** |
| Windows Release compilation | **Succeeded, 0 warnings, 0 errors** |
| Portable ZIP verification | **Succeeded** |
| Inno Setup compilation | **Succeeded** |
| SPDX SBOM / SHA256SUMS | **Generated** |
| Build attestation | **Created for ZIP, SBOM and SHA256SUMS** |
| Public Release / asset upload | **Succeeded, 4 uploaded assets** |

The release gate used the source-backed IEC-101 SICAM test fixture fix that stops after two observed accepted secondary RX responses rather than a 500 ms wall-clock race. Its original production `ResponseTimeoutMs = 180` was not increased; TX echo rejection, delayed echo evidence and checksum assertions remain. This automated fixture does **not** prove recovery under every physical SICAM serial timing condition. The tested commit was initially one commit ahead of `master`; [PR #134](https://github.com/masarray/ARIEC60870/pull/134) tracks reconciliation of that exact test-only fix.

**Existing evidence limitation:** `release-package.yml` on the tagged RC ran all xUnit projects with the same `release-gate.trx` file name, causing log warnings that earlier TRX files were overwritten. Per-suite pass counts above are grounded in the authoritative Package job log, but **the uploaded TRX artifact cannot be treated as a complete six-suite per-test archive**. A separate post-RC release-workflow fix changes the next build to one TRX per test project, preserving all evidence.

## Published asset identities

The following SHA-256 values are reported by GitHub Release's uploaded asset digest API and are corroborated by the Package job's checksum-generation step. Verify against the independently published `SHA256SUMS.txt` before executing a downloaded file.

| Asset | Size (bytes) | SHA256 |
| --- | ---: | --- |
| `ARIEC60870-v3.6.8-rc.1-win-x64.zip` | 69742607 | `b191159171c06f9b0d84316a119686334acbf56788c0a87498be910cd295ae5d` |
| `ARIEC60870-v3.6.8-rc.1-setup-win-x64.exe` | 70260126 | `d464c8114568a30c7eb9d9561e88840474c66ee6b521cedc444d1f23c25a9039` |
| `ARIEC60870-v3.6.8-rc.1-sbom.spdx.json` | 4712 | `4f81b619344f685c84ed556db4c4e8499ab1b17b88a1003121e8b24b7b40e180` |
| `SHA256SUMS.txt` | 315 | `8fcef4369cafd1343c90930a1b0ba1d75a043a1c1ea879e4771b4a8edfc109a3` |

Current provenance: [Attestation #65641316](https://github.com/masarray/ARIEC60870/attestations/65641316) covers the ZIP, SBOM and checksum file. The **installer EXE was not an attested subject in this already-published RC**. The post-RC workflow hardening adds an installer-specific attestation for later RC builds; it does not retroactively change this release.

## Acceptance status and scope boundaries

- **Proven in this release workflow:** Windows .NET Release compilation, 19 protocol smoke cases, 852 xUnit cases, ZIP structure, Inno Setup compilation, SBOM, SHA256, and release publication.
- **Validated on prior `master` UI CI baseline, not re-run as its own post-install RC exercise:** real-WPF 1400-frame viewport/Frame Trace acceptance, GUI packaging, snapshots. The RC's only divergence from that baseline is the test-only SICAM fixture.
- **Not proven by this RC automation:** physical clean install and in-place upgrade from v3.6.7 with a running Master, Windows Restart Manager handling, long-soak IEC-101 1200 bps, high-volume SICAM SOE FIFO, 2/4 independent COM simultaneous capture, physical relay IEC-103 interoperability, all-vendor IEC-104 reconnect, any production protection/control performance.
- RC binaries are **unsigned**. They are meant for authorized isolated engineering test benches, not live substation control.

## Public tester acceptance matrix

| Scenario | Evidence to capture |
| --- | --- |
| Clean Windows x64 installer / extracted portable | Windows edition, version, startup behavior, installer/ZIP hash, error dialogs |
| Upgrade from v3.6.7 while app closed / running | Previous and current version, preserved profiles/exports, process exit, uninstall registration |
| IEC-101 unbalanced 1200 bps (physical or simulated, specify which) | COM parameters, link address/width, FCB/FCV/ACD/DFC, TX/RX raw HEX, timeouts, echo |
| Class 1/2 and GI | GI ACTCON/ACTTERM and relevant Type/COT/CA, frame count, expected vs observed IOA |
| SOE burst, reconnect or FIFO saturation | Requested/injected events, observed events, outstation overflow diagnostics, missing/duplicated sequences |
| IEC-103 complete Type 2 and unsupported vendor/private data | Original 10-octet event payload, displayed DPI/RET/FAN/CP32/SIN, raw-only fallback |
| IEC-104 reconnect and malformed APDU/ASDU | TCP reconnect source, APCI sequence, exact HEX, decoder state, evidence preserved |
| Engineer Mode Frame Trace / Values / Events / Reports | Original HEX vs interpreter, IOA physical `IOA1→IOA2→IOA3`, structured `IOA3–IOA2–IOA1`, CP timestamps, Valid/Genuine/DST, OV/IV/BL/NT/SB, export parity |

Report problems using [GitHub Issues](https://github.com/masarray/ARIEC60870/issues/new/choose). Include build/version, source SHA, operating system, reproducible steps, serial/protocol widths and timing, **sanitized** original HEX, expected and actual behavior, and whether equipment was simulated or physically connected. Never share credentials, customer or substation identifiers, private IP allocations or site-specific process data.
