# ARIEC60870 — Engineering Handoff to Next ChatGPT Thread
Snapshot: **2026-10-28, Asia/Jakarta**. Scope: **professional IEC 60870-5-101 passive serial forensic monitor** within the existing ARIEC60870 desktop application.

> SOURCE OF TRUTH: `masarray/ARIEC60870`, **`master`**, audited commit **`748e235d363c42e3fa987e5d4c96ad7ba0cece4c`**. This is an observation at handoff time; FIRST refetch current master and open PRs. Do not overwrite newer progress or start from an older feature branch.

## 1. User objective and non-negotiables

Professional passive IEC-101 monitoring/diagnostics comparable to the useful ASE2000 V2 workflow, without borrowing vendor code or inventing electrical timing. Real case: two redundant RS-232 IEC-101 links between NUC/SCADA ETERA and Siemens SICAM A8000; communication established on both paths, but active side reportedly pauses for several seconds and alternates around every 30 seconds. Purpose is evidence-led investigation, not to assume a known root cause.

- SMART/optimized from the start; reliable, lightweight, maintainable, fast/stable; no naive patches or repeated trial/error. Prefer real milestones with verified CI and portable Windows GUI artifact.
- Preserve **Active Master** IEC-103/101/104 and existing premium calm WPF design tokens. **Do not create a separate monitor window, new navigation rail or redundant action button.** Use existing Setup protocol dropdown -> IEC-101 Passive Monitor, Frame Trace, Event Logs, Values, Findings, Capture, export and operator tools.
- RX-only sniff: never issue commands, GI or serial writes on any monitor COM. Software cannot guarantee physical noninterference: four independent, properly isolated/high-impedance RX taps must leave production TX, RTS, DTR, CTS, handshake and cabling undisturbed.
- Raw journal is authoritative and never sacrificed for UI speed. Bounded GUI preview can drop; full raw binary and forensic records must persist with verifiable integrity and candid uncertainty.
- 2 RX inputs = SCADA TX + RTU TX of **one** full-duplex link. 4 RX inputs = these two transmitter directions on each of separate **A and B** circuits.
- Link-status-only traffic on standby is **normal**, not itself failure; 30-second pauses or alternating frames are observations, not proof of NUC role transition. External NUC logs/IEC104 are independent evidence.
- Clean-room Apache-2.0 and engineering guardrails: read `AGENTS.md`, scoped AGENTS, `docs/OUTPUT_AND_PERFORMANCE_POLICY.md`.

## 2. Verified GitHub baseline / no duplicated work

Current merged master history (newest to older):
- **`748e235` / PR #36** — performance: reuse bounded per-source replay buffers, avoid per-record allocation in four-RX replay.
- **`2ad9a12` / PR #35** — fix live Passive Frame Trace flushing; preserve noise/Link Address width diagnostic visibility.
- **`d3c2e5b` / PR #34** — **P3 FOUR RX redundant A/B monitor already MERGED**: capture, profiles, GUI, journal/replay, per-circuit forensic engines.
- **`d58892e` / PR #33** — P2E five-second forensic failure timeline and durable `timeline-windows.jsonl`.
- **`d79a9c4` / PR #32** — P2D GI lifecycle (Type100 COT6/7/10) plus `gi-events.jsonl`.
- **`943bd87` / PR #31** — P2C verified read-only offline re-decode with Setup parameter override, preserving raw/manifest.
- **`5f03794` / PR #29** — IEC-101 correct E5 contextual short ACK / short no-data; prohibited A2 never accepted as valid NACK.
- **`d690505` / PR #28** — P2B request/reply, retry, timeout, FCB/FCV, unmatched response and durable `transactions.jsonl`.
- Earlier P0/P1/P2A: isolated RX-only serial journal; GUI integrated Setup/Trace/Events/Values; verified full binary replay; restored left-side capture/export tools.

**Do not resume the old `feat/iec101-p3-four-rx-redundant-capture-20261028` or `perf/iec101-p3-four-rx-replay-buffers-20261028` as baselines.** They were used for development and diverge from later merged master (comparison confirmed); newer merged fixes must not be overwritten. Scratch GitHub blob drafts from the earlier unfinished P3 iteration are not a release or the source of truth.

Only open PR observed at this snapshot: **#12** (independent dependency/Scorecard update), not an IEC-101 feature PR.

### Latest tested master CI / build
- Commit `748e235d363c42e3fa987e5d4c96ad7ba0cece4c`.
- [Windows CI #40627688308](https://github.com/masarray/ARIEC60870/actions/runs/40627688308) **SUCCESS**: 19/19 protocol smoke; **141/141 xUnit** (Core12 + Master44 + Monitor56 + Reporting5 + Desktop9 + Repository15), build and packaged GUI. Real `--passive-gui-smoke` invoked in CI.
- [CodeQL #40627688343](https://github.com/masarray/ARIEC60870/actions/runs/40627688343), [Repository hygiene #40627688318](https://github.com/masarray/ARIEC60870/actions/runs/40627688318), [OpenSSF #40627688276](https://github.com/masarray/ARIEC60870/actions/runs/40627688276): SUCCESS for that SHA.
- [GitHub Actions portable Win-x64 artifact](https://github.com/masarray/ARIEC60870/actions/runs/40627688308/artifacts/12790735054): `ARIEC60870-Passive-Monitor-P0-GUI-win-x64`, ~70 MB. Legacy artifact NAME says P0 but it was produced from **latest P3+fix+perf master**; judge by run SHA, not artifact name. CI log reports packaged artifact digest(s); independently check ZIP/embedded SHA256SUMS before field delivery. **No physical SICAM–ETERA FAT is claimed.**

## 3. Real current implementation and file map

### P3 two-/four-source capture
- `src/ARIEC60870.Monitor/PassiveCaptureOptions.cs`: legacy A `ScadaTx`/`RtuTx`, optional B `LinkBScadaTx`/`LinkBRtuTx`; `StreamCount=2|4`, validation: all COM unique, B must be a complete pair, valid UART settings, one profile for both links.
- `src/ARIEC60870.Monitor/PassiveMonitorSession.cs`: dedicated reader per RX, bounded channel and single disk journal writer, per-source offset/hash/CRC, scanning, durable transaction/GI/timeline evidence, bounded notification channels. Exact mapping: **A SCADA=stream00, A RTU=01, B SCADA=02, B RTU=03**. All configured tap inputs must open; stop/error integrity is reported.
- `src/ARIEC60870.Monitor/PassiveJournalFormat.cs`: APM1 per-read binary records (timestamp/stream ID/sequence/source byte offset/CRC32); format preserved for backwards compatibility.
- `src/ARIEC60870.Monitor/PassiveJournalVerifier.cs`: checks both 2-stream and 4-stream complete manifest/file sets, per-record continuity, CRC and SHA256. An incomplete or corrupt session must not be reported as verified.
- `src/ARIEC60870.Monitor/PassiveJournalReplay.cs`: read-only complete verification, then deterministic merge of 2/4 USB-host timestamp-ordered streams; independently preserves per-source order and supports profile override. PR #36 optimized replay buffer reuse.
- `src/ARIEC60870.Monitor/PassiveFt12Scanner.cs`: one bounded incremental FT1.2 scanner per physical transmitter; preserves partial/noise evidence.
- `src/ARIEC60870.Monitor/PassiveIec101TransactionCorrelator.cs`: pairs request/response by circuit, direction and link address. **Addressless E5 may not match across A/B.**
- `src/ARIEC60870.Monitor/PassiveIec101GiTracker.cs`: Type100/COT6 activation, 7 confirm (including negative), COT20–36 data and 10 termination, without cross-circuit matching.
- `src/ARIEC60870.Monitor/PassiveIec101FailureTimeline.cs`: five-second windows, per-circuit evidence counters, raw source offsets, bounded compressed no-observation intervals.
- `src/ARIEC60870.Monitor/PassiveReplayProfile.cs`: validated link-address 0/1/2 octets, COT1/2, CA1/2, IOA1/2/3, mode and gap.
- `src/ARIEC60870.Desktop/MainWindow.xaml`, `Features/MainWindow.PassiveMonitor.cs`, `Features/MainWindow.PassiveReplay.cs`, `Features/MainWindow.SetupPreferences.cs`: existing GUI and design tokens. Setup has optional **Also monitor redundant Link B (4 RX ports)** checkbox; Save/Load preferences, Frame Trace/Values/Event Logs, existing Capture and diagnostic tools. Source-specific labels and circuit-qualified IOA keys avoid cross-link overwrites. PR #35 fixed live Frame Trace flush problem.
- Tests in `tests/ARIEC60870.Monitor.Tests/` cover two/four-source journal/replay and protocol state; WPF smoke in existing GUI.

### On-disk capture
```
session directory/
  manifest.json             # finalized 2/4 streams, UART profiles and integrity
  stream-00.bin              # A SCADA TX observed on RX0
  stream-01.bin              # A RTU TX observed on RX1
  stream-02.bin              # B SCADA TX observed on RX2 (4-RX only)
  stream-03.bin              # B RTU TX observed on RX3 (4-RX only)
  frames-XX.jsonl            # derived per-source framing
  transactions.jsonl         # derived IEC-101 link exchanges
  gi-events.jsonl            # derived GI lifecycle
  timeline-windows.jsonl     # derived per-circuit failure timeline
```
Raw `stream-XX.bin` and `manifest.json` are authoritative; derived JSONL can be recreated. Do NOT conflate this journal folder with older `.ariec` UI evidence export format.

## 4. Immediate next milestone (P4 / next thread)

**P4A: evidence navigation and search, not another monitor engine.** Investigate `master` first and pick a cohesive implementation:
1. Add scalable, **indexed or paged** read-only navigation of multi-hour/multi-day `timeline-windows.jsonl` and transaction/GI records. Search by UTC window, Link A/B, physical stream, link function/FC15, retry, timeout, GI and raw offset. Avoid `File.ReadAllLines` or unbounded WPF rows for large archives.
2. Allow operator to jump **timeline anomaly → correlated transaction → exact raw frame/bytes**, preserving stream ID, original offset and decoder profile. Reuse existing Frame Trace/Findings/inspector/left-side capture tools; no unnecessary new window/tab/button.
3. Provide small honest per-link comparison (A/B observed activity, Class1/2/GI/FC9 supervision, last valid reply, retries/timeouts, periods of silence). **Inference labels** must separate `Observed on Link B` from `NUC switched to B`; actual switch requires contemporaneous NUC/SCADA external evidence. Do not infer from 30s alone.
4. Add explicit indexed tests for huge journals, overlapping A/B, gap/partial/noise, corrupted or incomplete archive, profile override, seek correctness, cancellation and bounded memory; preserve legacy 2-RX replay.
5. Explore separately: link-layer unknown FC/FC15 vs IEC-101 application refusal COT44–47 and GI lifecycle around reported 30s alternations; keep diagnosis evidence-led.

**P4B physical acceptance & reliability (must NOT claim complete prematurely):**
- Four isolated receive-only RS-232 taps electrically validated for noninterference, preservation of RTS/DTR/CTS/handshaking on plant; correct polarity/signal/COM assignment.
- ≥2h continuous 2/4-COM capture with measured CPU, working set, disk throughput, queue backpressure, source byte integrity, unplug/reconnect and full/disconnected drive behavior; compare reference analyzer trace; verify raw SHA counts, application-loss/driver errors and how incomplete capture is flagged.
- Check field UART (e.g. 8E1/baud) and Link Address 2 octets from **actual interoperability config**, not assumption.
- Windows USB receive completion timestamps cannot prove electrical **33-bit idle** or sub-ms inter-circuit ordering. Hardware timestamp/logic analyzer required for line-level claims.

## 5. Required engineering workflow

1. FIRST fetch latest `master`, recent merged/open PRs/issues and `AGENTS.md`; use latest merged base. Don't overwrite simultaneous thread progress.
2. Read P3 ARD `docs/IEC101_PASSIVE_DUAL_CIRCUIT_P3.md`, P2E `docs/IEC101_PASSIVE_FAILURE_TIMELINE_P2E.md` and `docs/OUTPUT_AND_PERFORMANCE_POLICY.md`. Parent tracking issues [#19](https://github.com/masarray/ARIEC60870/issues/19) and [#30](https://github.com/masarray/ARIEC60870/issues/30).
3. Work in isolated feature branch; protocol analyzers in Monitor (no WPF dependency), UI adapters only in Desktop; no active transport TX modifications.
4. Prefer full-flow, meaningful milestone over tiny independent patches. Include synthetic deterministic field fixtures, real CI, WPF smoke and reliable portable Win-x64 artifact from tested head.
5. Check latest master again just before merge; evaluate CI Release, xUnit, 19 protocol smoke, GUI, CodeQL, hygiene and hashes. Never claim a hardware test or produced binary without evidence.
6. Update this handoff/issue with new merged commit, test results, next blockers for next thread.

## 6. Copy/paste prompt for next thread

```
Continue ARIEC60870 from latest GitHub master, not old chat branches.
Read docs/HANDOFF_NEXT_THREAD_ARIEC60870_20261028.md and docs/IEC101_PASSIVE_DUAL_CIRCUIT_P3.md, AGENTS.md, latest PRs/CI, and reconcile all merged work.

Important: P3 four-COM A/B RX passive monitor, P2B transaction correlation, P2C replay profile override, P2D GI, P2E timeline, PR #35 live trace fix, PR #36 replay optimization are ALREADY merged. Do NOT redo them or remove existing left-side tools/Setup/Frame Trace/Event Log/Values/Findings.

Implement the next high-value milestone: scalable evidence-first indexed failure timeline navigation with exact jump-to-raw frame/transaction evidence for both Link A/B, bounded CPU/RAM, no WPF per-frame blocking and no serial TX. The 30-second NUC active/standby alternation is an observed field symptom, NOT a proven IEC-101 root cause. Preserve legacy two-COM behavior, physical tap safety, exact raw journal integrity, and useful GUI within existing design tokens.

Work as optimized senior IEC-101 architect: plan/design/implement/tests/CI/package/merge. Provide real PR, green Windows results and GitHub artifact; never claim physical FAT until tested.
```

## 7. Links
- Repository: https://github.com/masarray/ARIEC60870
- P3 merged PR #34: https://github.com/masarray/ARIEC60870/pull/34
- Live Frame Trace hotfix PR #35: https://github.com/masarray/ARIEC60870/pull/35
- Replay performance PR #36: https://github.com/masarray/ARIEC60870/pull/36
- Tested master Windows CI: https://github.com/masarray/ARIEC60870/actions/runs/40627688308
- Tested master GUI artifact: https://github.com/masarray/ARIEC60870/actions/runs/40627688308/artifacts/12790735054
