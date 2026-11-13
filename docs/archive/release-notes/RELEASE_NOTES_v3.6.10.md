# ArSee v3.6.10 — Windows x64 stable release

## Better forensic investigation of IEC-101/103/104

ArSee now makes it easier to move from a received process event to the **original physical frame evidence**. The Event Journal preserves historical paging while context-aware live-follow tracks new activity without taking the engineer away from an earlier page. **Open Original Frame** navigates to the correlated TX/RX original trace and frame details. When the original TX is missing, the analyzer says so instead of inventing evidence. These improvements incorporate merged PRs [#197](https://github.com/masarray/ARIEC60870/pull/197), [#198](https://github.com/masarray/ARIEC60870/pull/198) and [#199](https://github.com/masarray/ARIEC60870/pull/199). The compact header branding position was corrected by [#200](https://github.com/masarray/ARIEC60870/pull/200).

## Bay-aware SCADA signal names

The bundled, editable utility IEC-101/104 I/O example now resolves all **31** table points with bay context: **FEEDER, KOPEL, TRAFO**, appropriate CB status, Local/Remote and command distinctions, BUSBAR voltage status and four missing RTU alarm points. No longer interpret every CB as an indistinguishable “CIRCUIT BREAKER OPEN/CLOSE”. Legacy IOA lookup refuses to assign a trusted signal name when CA or IEC information-type family is wrong. The received CA, IOA, Type ID, raw bytes and signal state remain authoritative forensic evidence; display labels are separately supplied by the operator's selected mapping profile. See [#201](https://github.com/masarray/ARIEC60870/pull/201).

This change updates the **bundled sample profile**, not an existing custom profile on the engineer's computer. Select the updated bundled profile in Setup if its mapping fits your approved RTU I/O database; never treat this example as a universal vendor mapping or operate a breaker based on an unverified name.

## Safer low-speed IEC-101 frame reception

At 1200 bps, a legitimate FT1.2 reply may need longer to complete than the initial response-start timeout. ArSee now uses a **bounded, baud-aware frame completion window** once a physical FT1.2 start is observed, without artificially lengthening a truly silent response. The single-link Master retains one framing reader across poll exchanges within an open COM epoch; dual A/B channels use their own per-link readers. Truncated framing is accounted separately from validated responses; a stale ambiguous E5 after a broken prefix cannot silently establish a healthy link. See [#202](https://github.com/masarray/ARIEC60870/pull/202).

These are **software and simulated-transport improvements**, not proof of successful physical SICAM A8000 interchange.

## Windows installer and portable edition

- The per-user Windows Setup supports the operator-approved stable updater introduced in v3.6.9; active IEC protocol sessions are never forcibly terminated or automatically restarted after updates.
- The portable Windows x64 ZIP remains a separate, self-contained download. It does not self-replace.
- Installer/setup, ZIP, SHA256 checksums and SPDX SBOM are published from the matching verified source by the repository's guarded workflow.
- **Windows binaries are currently unsigned.** Confirm the GitHub origin and `SHA256SUMS.txt`; the published checksums are not equivalent to a pinned Authenticode publisher signature.

## Evidence gate and remaining field tests

This stable publication is limited to code that has passed the repository's Windows source, protocol, full xUnit, real-WPF executable, package integrity and **same-commit native Windows installer/updater FAT** workflow gates. Check the release's actual workflow run and published checksums; a green PR is not a substitute for the final release build.

Draft [IEC-101 shared per-link transaction/FCB core PR #145](https://github.com/masarray/ARIEC60870/pull/145) remains **unmerged**. Its independent transaction, reconnect and redundancy changes require further semantic validation; do not interpret this release as claiming them.

**FIELD FAT PENDING:** real SICAM A8000 at 1200 bps; source-side comparison of 60/500/2,000 SOE events, missing/duplicate/filtered evidence, physical COM unplug/replug, dual A/B failover and command/GI lifecycle. Use an isolated approved bench for active controls. No energized/production SCADA interoperability is certified by these simulated tests.

Official downloads: [ArSee GitHub Releases](https://github.com/masarray/ARIEC60870/releases).
