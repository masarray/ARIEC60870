# Raw Frames — Engineer Readability Contract

Status: UX-02 / TRACE-02 implementation guardrail, 2026-11-11.
Scope: WPF Raw Frames presentation only. The original Core decoder and physical-frame ledger remain authoritative.

## Stable invariants

1. Each displayed top-level row represents one physical frame. Original hex, sequence, host arrival timestamp and explicit TX/RX direction remain visible. The viewer never creates one fake physical row per IOA.
2. TX uses pale blue (#EAF3FF), RX pale green (#EAF7F0). Real integrity or quality problems may override with a restrained attention tint. Selection and hover remain readable; TX/RX text survives without color.
3. IEC-101 ACD=1 is Class 1 data pending, not a corrupt frame or negative acknowledgement. Show the separate dark-amber sentence "Data Class 1 Pending (ACD): TRUE", not an error state. DFC, invalid quality and negative confirmation remain separately diagnosed.
4. Inline interpreter title, per-IOA signal, COT, timestamp and flags use one compact 11.2-DIP monospace text size. Avoid oversized cards, heavyweight bold, extra user modes and hover tooltips on evidence rows.
5. Each decoded IOA carries its own value, IEC type, source quality, and, when actually encoded, its own CP56Time2a timestamp and IV/GEN/DST flags. Print the device wall-clock as DD-MM-YYYY HH:mm:ss.fff without inventing time zone. Missing or partial time must say so, not borrow from another IOA or host arrival time.
6. Structured IOA3/IOA2/IOA1 labels are the high-to-low human decomposition of the integer IOA. Original wire octets run low-to-high. SQ=1 subsequently implied addresses must remain explicitly marked derived.
7. Preview the first three signal summaries and show "+N more signals"; all source-decoded objects remain accessible in Frame Details and original archives. Keep virtualization and original event retention untouched.
8. "Copy Row (As Displayed)" is a plain-text copy of the exact bound visible row fields, with the current bounded preview and line breaks. The distinct "Copy Selected Frame (Full Evidence)" remains intact. Right-click respects existing multi-selection or selects the pointed row.
9. Display labels cannot become protocol evidence. Missing quality stays "Not provided", malformed values cannot appear Good, and archived frames need valid source-backed decode before an IOA/time claim.
10. Changes must stay in a single shared formatter/row projection and existing XAML template; do not introduce another parser, competing state machine, screen mode, or per-frame UI worker.

## Regression and delivery gate

- Deterministic xUnit: FrameTraceEngineerReadabilityTests, ObservationInterpretivePresentationTests, FrameInterpreterScanTests, FrameTraceVisualPolicyTests, SmartRedundancyUxTests and ProtocolTruthFrameTraceTests.
- Real WPF acceptance: CP56 own-device-time/GEN, visible per-object lines, source cause, selected inspector, narrow and wide viewport and original HEX.
- Merge only after exact-HEAD Windows build, format, all xUnit, screenshots, CodeQL, dependency review and hygiene. Inspect actual screenshots, not just a green runner.
- This UX work is not evidence of Siemens/SICAM interoperability or active IEC-101 FAT. Keep the engine reliability P0 PR independent.
