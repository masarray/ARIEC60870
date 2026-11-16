# ArSee v3.6.12 — Consistent IEC-101/104 mapped signal names (Windows x64 stable)

## Engineering usability: one signal identity across the application

An IEC-101/104 process point from the active IOA profile now keeps the same engineering signal name in the live **Events**, **Values**, **Frame Trace** and **Frame Inspector**, as well as paged **Event History**, history search, offline capture review and CSV exports. This includes the bundled sample profile's **CA 105 / IOA 16712689 / TI 31: FEEDER CB STATUS**. Previously this point could appear as the raw fallback `IOA 16712689` despite an available configured name.

Value rows re-project when a profile is changed, and history pages re-apply the profile's current names without editing the original source evidence. A saved imported profile now restores from its actual local file path; a sanitized path remains only a presentation value.

Read-only name recovery tolerates a single-station process signal with a uniquely matching IOA and compatible signal kind where a received CA or timed Type ID disagrees with the profile. This **never rewrites physical CA/IOA/Type**, never increases GI proof coverage, and is deliberately rejected on conflicting source identities, multi-station ambiguity and command identity. Unmapped points such as bundled-profile-absent **IOA 388725** remain explicitly unmapped; they are not guessed to be other signals.

Command events can show the safely mapped descriptive name while preserving original command target and evidence. Command feedback lookup remains strict by CA/IOA and monitoring type. The release incorporates the reviewed fixes and regression cases from [PR #210](https://github.com/masarray/ARIEC60870/pull/210).

## Preserved IEC-101 work from the concurrently developed previous release

This build also retains all public **v3.6.11** IEC-101 service-correlated reply validation from [PR #208](https://github.com/masarray/ARIEC60870/pull/208), the earlier v3.6.10 forensic Event Journal, original TX/RX frame inspection, bounded 1200-bps FT1.2 receive, 31-point bundled profile, installer updater safety and the existing IEC-103/104 workspaces. The bilingual public website/download documentation contributed by [PR #211](https://github.com/masarray/ARIEC60870/pull/211) remains part of the mainline history.

## Validation and physical field limitations

The Windows x64 installer and portable ZIP require a **successful exact-commit** GitHub Actions package build, six xUnit suites, protocol smoke tests, real-WPF executable/Frame Trace acceptance, SHA-256 checksums, SPDX SBOM and build provenance, plus independently successful Windows installed-updater FAT before stable publication. Verify the specific successful workflow runs; merging the code or passing PR-only CI does not qualify public installer bytes.

**FIELD FAT PENDING:** Actual SICAM A8000 / SCADA site verification of the corrected CA 105 / IOA 16712689 display, cross-device IEC-101 interoperability at 1200 bps, 60/500/2,000-event SOE FIFO completeness, physical COM reconnect, dual-link failover and breaker GI/SBO command lifecycle. No physical IEC conformance certification is claimed. The draft [#145](https://github.com/masarray/ARIEC60870/pull/145) shared FCB/retry/core rearchitecture remains **unmerged**.

Windows executables are currently **unsigned**. Download only the official GitHub Release assets and verify `SHA256SUMS.txt`. The installed updater needs operator approval and does not force-stop active IEC capture; portable ZIP does not self-update.
