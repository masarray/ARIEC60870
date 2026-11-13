# IEC-101 P0 reconciliation checkpoint — 2026-11-13

## Live baseline and scope

- Verified `master` before work: `fe3b760b4f66449ffb020c8dd68c56135370c9b5`.
- Draft PR [#145](https://github.com/masarray/ARIEC60870/pull/145) head: `78b09bafcba9e201322a835a4ea6fb204d0b74c7`, **43 ahead / 44 behind** master; last common ancestor `0cedf8891bf28954b862b7912e0db29f68aebc68`.
- PR #145 changes 38 paths relative to common ancestor. Master has 101 changed paths in the opposite direction, **2 shared paths**: `src/ARIEC60870.Desktop/Features/MainWindow.Session.cs` and `tests/ARIEC60870.Monitor.Tests/PassiveMonitorP0Tests.cs`.
- This is a source-diff reconciliation, **not** evidence of full IEC-101 interoperability or a valid bulk merge.

## Semantic ownership matrix

| Surface | Draft #145 contents | Newer master contents / risk | Integration disposition |
|---|---|---|---|
| `Ft12StreamReader` | bounded baud-aware start/completion budget, physical byte/expired fragment counters, E5 suppression after aborted fragments | same canonical Core framer already used in master; original completion deadline could truncate a 1200-bps variable reply | **Port separately** in this milestone, no new link owner; wire configured baud only on IEC-101 single and A/B. IEC-103 default reader unchanged |
| `Iec101PerLinkTransactionEngine`, `Iec101OwnerStamp`, `Iec101SecondaryResponseValidator` | pending exchange/physical epoch, FC-compatibility/FCB advances, one owner per link | master has evidence-only `EventFrameExchangeTracker` (#199), a different **forensic** concern; cannot equate trace pairing with wire transaction authority | **Hold #145 draft** until one-owner semantics, delayed identical FC11, status/E5, duplicate responses and cancellation are reviewed together |
| `Iec101MasterSession` | polling/retry/DFC, GI and control handling, physical reopen, counters | #197–#199 add Event Journal/original TX/RX attribution; replacing current session wholesale risks provenance and replay assumptions | **Only select small reader constructor wiring**. Do not port session-wide diff blindly |
| `Iec101DualLinkChannel`, `Iec101DualLinkRedundancySession`, link/physical recovery | per-channel epochs/port recover and owner generation, active/standby decisions | #199 forbids cross-source request/response evidence and unverified command replay | **Only select reader constructor wiring**. Full failover/reconnect changes remain gated |
| `MainWindow.Session.cs` | profile-owned post-switch GI policy | newer master contains GUI/session changes; shared path | **Manual semantic merge required**; no restoration of old GUI, profile/default, or Command Dock assumptions |
| `PassiveMonitorP0Tests.cs` | completion-driven capture-before-corruption fixture | newer master changed acceptance-test/capture behavior; shared path | **Re-evaluate both fixtures**, do not overwrite master test |
| `Iec101BoundedFifo`, class-2 fairness/burst tests | bounded queue and self-observation of scheduling | count of frames is not proof of received/valid/delivered event sequences | **Pending end-to-end 60/500/2000 burst evidence**, separate physical RX, frames, decoded objects, duplicates and losses |
| Release, installer, GUI, Event Journal, CI | not the focus of #145 | newer master includes #197/#198/#199/#200 and v3.6.9 publication metadata | **Preserve master**; no release or installer changes in this milestone |

## Isolated FT1.2 milestone contract

1. Do not replace the canonical Core `Ft12IncrementalFramer`, change FCB/FCV, infer GI completion, or enable control retransmission.
2. At 1200 bps, allow a complete variable FT1.2 reply to finish after the **first-byte response timeout** only after a physical `0x10`/`0x68` start, with a one-time, baud-derived upper bound. Pure silence retains the original deadline.
3. Report physical octets, expired partial frames and expired partial bytes separately from accepted/decoded frames.
4. A truncated frame invalidates the current assembly; a following unaddressed `E5` is ambiguous until a verified addressed frame re-establishes the boundary.
5. Reader construction is link-specific: IEC-101 single and A/B pass their configured baud, while IEC-103 stays on the existing default.
6. Tests run using deterministic paced/fragmented transports without physical RTU claims.

## Gate and remaining P0

- Validate exact PR-head Windows Release build, targeted Master tests, full relevant regression and CodeQL before merge. Record PR and `master` CI separately.
- **FIELD FAT PENDING:** SICAM A8000 COM unplug/replug and driver write timeout, late FC11, multi-vendor status/FCB behavior, dual-link failover, GI staged ACTCON/ACTTERM, SBO/DO readback, and serial 1200-bps SOE 60/500/2000 bursts with actual source-side event totals.
- The reported successful FC0 start is not evidence that any of these outstanding FAT cases passed.
- Draft #145 remains open and **must not be merged as a bulk patch**. Continue from the current master and compare cumulative changes with the newly isolated milestone before next semantic port.
