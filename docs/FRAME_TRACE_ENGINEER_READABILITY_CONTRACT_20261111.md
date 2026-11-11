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


## 2026-11-11 follow-up — DPI, timestamp and engineering density

- Missing CP56/object time reads "No Timestamp" (never "Timestamp: No Timestamp"), never implying a device or decoder error. Explicit partial CP24 time remains distinct from missing CP56.
- For TI 3/4/31 only, when canonical typed DIQ is available: 00 = "Invalid [00]" (intermediate state), 01 = "OFF [01]", 10 = "ON [10]", 11 = "Invalid [11]" (indeterminate/fault state). The word Invalid in these **state** labels must not be conflated with quality IV (bit 7); present source quality independently. No inferred DPI from human text when DIQ missing.
- Raw wire lane 24% / frame interpreter 76% when paired; measured width below 840 DIP stacks rather than clipping the header. Frame Details right dock narrows to a 3 : 1.1 star ratio with 250 DIP minimum and retains operator splitter. No novel panel or user-facing mode.
- Every TX frame retains a narrow blue direction stripe (#2563B8), every RX frame a green stripe (#16805D), including selected, hovered and attention-overridden rows; raw HEX text uses the same typed direction ink. Row background continues pale-blue/pale-green with semantic attention override.
- Regression proof: typed four-state DP fixtures, invalid-quality orthogonality and missing-DIQ guard, no-timestamp format, viewport geometry thresholds, true-WPF direction stripe and paired-vs-stacked/inspector layout. Exact-head Windows CI, CodeQL and human review of screenshots remain merge gates.

## 2026-11-11 correction — operator-directed headline hierarchy

- IEC-101 decoded valid GI frame top heading: `GI Data · Single-Point · Link 105 · CA 1`, with original source Link/CA displayed before per-IOA value/quality/COT. A valid ASDU CA is required; absent physical link is never fabricated. Bad/undecoded ASDU cannot present a default CA 0.
- Identity is no longer printed as a redundant lower WPF paragraph when the heading already includes it. Plain-text row clipboard mirrors exactly that layout; separate typed identity still available to selected inspector and tests.
- Missing CP56/object time: `No Timestamp`. Typed CP56 valid: `Timestamp Valid`; typed invalid: `Timestamp Invalid`; CP24/other partial source text: `Timestamp Partial` with limitations. Never treat host arrival time as device timestamp.

## 2026-11-11 field correction — text hierarchy, spacing and severity

- Raw Frames object first line uses `IOA 8388715 [ IOA3: 128 · IOA2: 0 · IOA1: 107 ]`. Exactly one space follows each structured label; underlying raw hexadecimal and high-to-low logical address decomposition are unchanged.
- Every typed object detail begins on the next line with `  ↳ TI-30 Single Point Signal · Quality: ...` to visually distinguish child detail from parent address. The plain-text row clipboard uses the identical formatter.
- All seven right-lane interpreter blocks (title, objects, overflow, identity, cause, ACD, diagnostics) use fixed **17.5 DIP LineHeight** with `LineStackingStrategy=BlockLineHeight`. Subordinate paragraphs use a uniform **4 DIP** top gap. Dynamic row heights reflect the real count of previewed objects; no extra panels, modes, or decoder work.
- ACD=1 is **pending Class1 data** and uses dark amber `#A65B00`, never the error-red diagnostic. The latter still signals negative COT, invalid data/quality and genuine fault.
- The field screenshot has configured Link 105/CA105, positive TI100 COT7/10 at CA105, GI-related process ASDUs at CA1, and negative TI100 COT7 at CA1. Never mark GI105 coverage complete based on CA1 objects or silently retarget commands; investigate the CA mismatch separately using physical evidence.
