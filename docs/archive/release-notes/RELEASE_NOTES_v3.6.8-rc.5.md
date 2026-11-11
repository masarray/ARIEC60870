# ArSee / ARIEC60870 v3.6.8-rc.5 — Performance, Reliability & Unified Event Journal

**Public Release Candidate · Windows x64 · 12 November 2026.** This prerelease packages the tested current `master` Event History/Journal improvements for field engineers. It remains a test candidate, not a certified IEC conformance tool or proof that a particular RTU transmitted every generated event.

## Downloads and integrity

- **Windows per-user installer**: `ARIEC60870-v3.6.8-rc.5-setup-win-x64.exe`.
- **Portable Windows ZIP**: `ARIEC60870-v3.6.8-rc.5-win-x64.zip`; extract the entire folder first.
- Verify `SHA256SUMS.txt`; an SPDX SBOM and build provenance attestations accompany the release. Windows SmartScreen may warn about unsigned builds.
- The release workflow tests the exact packaged Windows executable with real WPF acceptance and publishes screenshots/metrics and regression results as workflow artifacts.

## What's new since v3.6.8-rc.4

### P0 Event History reliability — merged PR #167

- Event history serialization, SHA-256 and disk writes run in a dedicated single background worker instead of blocking the WPF Dispatcher.
- A **bounded 4,096-item** session queue with up to **64 events per batch** provides explicit saturation/failure accounting; this is **not** a 4,096-event historical storage limit.
- Stable local Event # numbering, indexed/pending/unindexed distinctions, read-side integrity checks, and preserved source/original-frame references. On queue saturation or disk failure, indexing stops admitting new records instead of silently inventing a complete history.
- Avoid storing identical original Raw HEX twice. Pages are loaded off the UI thread.
- Regression coverage includes 2,000 / 10,000 / 50,000 synthetic events, quota failures, concurrent reads and writes, and recovery of original frame evidence. Synthetic load does not measure the physical 1,200-bps SICAM link or RTU FIFO completeness.

### One compact Event Journal — merged PRs #168–#171

- **One Event Log grid**, always backed by indexed event-history pages: no misleading `420 visible` or separate Live/History toggle. The 420-event internal preview is not a cap on indexed session history.
- 100-event page navigation, Previous/Next/Latest, search, Include/Exclude filters across full indexed history, and CSV export of current filter results or all indexed events.
- Background, cancellable and incremental query work; filtered scans use bounded batches, while opening an unfiltered history does not scan all records.
- User-selected historical pages remain pinned while new events arrive; Latest follows the newest matching events.
- CSV export streams in batches, publishes only after success, and guards spreadsheet formula injection.
- Original frame navigation and compact responsive toolbar WPF regression verification retained.

### Other merged IEC-101 and diagnostics improvements already in master

- Earlier master updates include active IEC-101 Reset Remote Link default on fresh connection (saved explicit OFF remains supported) and vendor-specific TI135 raw evidence handling. No unmerged shared transaction/FCB core is included in this RC.

## Software acceptance and boundaries

- Release source: `master@bc4e1ade57f5a5e09e7bc65e8de7e78c14fc95db`, plus this release-notes-only branch commit.
- Pre-release CI reference: [master Windows CI #43082758541](https://github.com/masarray/ARIEC60870/actions/runs/43082758541): **907/907 xUnit**, **19/19 protocol smoke**, real-WPF viewport and GUI packaging **passed**. [CodeQL #43082758753](https://github.com/masarray/ARIEC60870/actions/runs/43082758753) and repository hygiene passed.
- This RC's own `Package` workflow independently performs warning-gated Windows Release build, protocol and xUnit suites, full Windows application packaging, real-WPF GUI and 10,000-frame HOLD/AUTO acceptance, installer compilation, integrity checks, SBOM and attestation **before** publication.
- Session Event Journal history is temporary, not an automatically permanent capture file. Do not equate indexed process events with device-generated SOEs or UART bytes.
- [Draft PR #145](https://github.com/masarray/ARIEC60870/pull/145) shared IEC-101 transaction/FCB and dual-link hardening remains **excluded and under review**. True physical SICAM A8000 1,200-bps 60/500/2,000-SOE FIFO, COM unplug/reconnect and multi-vendor FAT are not certified by this release. Do not use against production substations without authorization and test isolation.

## Practical RC checks

1. Verify SHA256 and version, then install or fully extract portable ZIP.
2. Open Event Log and confirm a single grid, concise toolbar and 100-row pages; scroll/search old events after more than 420 events are indexed. Check Previous/Next/Latest and Include/Exclude.
3. Export all events and filtered events to CSV; verify event order, original evidence and any explicit unindexed status.
4. Exercise Trace HOLD/AUTO and original frame navigation; the held viewport should not jump when newer frames arrive.
5. On a safe simulator or isolated, authorized field bench only, compare raw TX/RX IEC-101/103/104 traffic against decoded evidence and independent device counters. Record version, exact COM/handshake, timestamps and sanitized logs when reporting a bug.

Feedback: https://github.com/masarray/ARIEC60870/issues

**Publishing policy:** Public prerelease assets are immutable once published; use a new RC number for subsequent changes.