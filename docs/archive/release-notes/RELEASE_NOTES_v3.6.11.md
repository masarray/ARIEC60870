# ArSee v3.6.11 — IEC-101 transaction reply validation (Windows x64 stable)

## Matched IEC-101 secondary responses, without fabricated link health

IEC-101 unbalanced Master (single-link and dual A/B) now validates the **pending primary link-service function** before accepting a checksum-valid secondary frame as the current transaction's reply. A delayed secondary FC11 link-status response cannot complete an unrelated FC3 GI/command or FC10/11 Class 1/2 poll. Rejected frames stay observable with their physical bytes and diagnostic reason; a mismatched response never advances the FCB.

A primary FC9 link-status probe requires an addressed FC11 secondary reply. An addressless E5 cannot establish the first outstation identity or satisfy FC9, and a standalone A2 is retained only as unsupported raw evidence rather than treated as a valid secondary NACK. Dual A/B supervision does not treat DFC-busy observations as proof that a standby link is healthy and promotable.

The simulated IEC-101 transport and deterministic single/A/B test fixtures have been brought into line with this exchange policy, with positive/negative cases for wrong-service, ACK, NACK, busy, checksum and status correlation. One old scripted GI test previously sent application-data FC8 **directly as the secondary answer to primary FC3**; that is now an explicitly rejected fixture, not incorrectly presented as confirmation of a complete application GI lifecycle.

These changes integrate selected, reviewable semantics from the ongoing draft [#145](https://github.com/masarray/ARIEC60870/pull/145) into already-shipped v3.6.10 code without wholesale merging its stale branch. See [#208](https://github.com/masarray/ARIEC60870/pull/208).

## Preserved v3.6.10 capabilities

- Event Journal historical paging, context-aware live following, and source-backed **Open Original Frame** TX/RX evidence.
- Bay-aware 31-point bundled IEC-101/104 example IO mapping with conservative CA/Type checks.
- Bounded, baud-aware FT1.2 receive at 1200 bps, persistent single-link reader and dedicated A/B readers.
- Existing desktop workspaces and Command Panel; independent IEC-103/104 and receive-only IEC-101 monitoring.

## Safety and test boundaries

This release **does not** include the complete shared per-link transaction/retry/FCB architecture, automatic physical COM reconnect recovery, independently proven GI/SBO application lifecycle, or physical redundancy FAT from unmerged draft #145. A correct link-layer ACK does not prove a successful breaker action, GI completeness or event FIFO delivery.

**FIELD FAT PENDING:** SICAM A8000 at 1200 bps, identical port/adapter cross-check, 60/500/2,000-event burst accounting, RTU FIFO/overrun diagnostics, real COM unplug/replug and dual-link A/B failover. These changes are software-engineering improvements; no IEC conformance, energized-site suitability or physical multi-vendor interoperability is certified by simulated tests.

## Official Windows x64 distribution and release gate

- Per-user Windows Setup, portable ZIP, SHA-256 checksums and SPDX SBOM are distributed from [official GitHub Releases](https://github.com/masarray/ARIEC60870/releases).
- Source-to-package integrity, all xUnit suites, 19 protocol smoke checks, real WPF executable acceptance, and the exact-source Windows native updater FAT are mandatory prior to publication. Refer to the release's actual GitHub Actions run for evidence; PR checks alone are not a completed release gate.
- The installed stable updater is operator-approved and never forcibly interrupts live IEC capture. Portable ZIP does not self-replace.
- Windows executables are currently **unsigned**. Confirm official release origin and verify `SHA256SUMS.txt`; checksums are not an Authenticode publisher signature.
