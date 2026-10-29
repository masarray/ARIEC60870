# R1–R2 — Durable Command Forensics & Streamed Capture Review

Date: 2026-10-29. Requirement: CMD-01/CAPTURE-01/TRACE-01 in canonical IEC10X PRD/ARD. This is incremental engineering work, not a field-certified conformance claim.

## What the engineer sees

In existing IEC-101 **Passive Monitor**, capture SCADA TX and RTU TX (two RX taps, optionally A/B four RX). The Frame Trace keeps one physical frame per entry. The scrolling list uses short control summaries, for example **NEG · SELECT · COT 7 · CA 1 / IOA 123**. Select the row for full typed COT, source offset, exact HEX and explanation in the existing Inspector, or Ctrl/Shift + Copy Selected Frame. No new Lab mode, extra panel or heavy animation.

A negative COT7/P-N=1 is an observed application-level negative activation confirmation. An RTU's exact internal interlock or local/remote cause cannot be known from that COT alone. ACTCON, ACTTERM and link ACK do not establish actual switchgear movement. Wrong physical Link A/B, wrong address, wrong command echo, overlap, TEST bit or corrupt frames never become accepted physical proof.

## R1 authoritative source

The passive **capture writer** derives **controls.jsonl** from the shared typed Master IEC10x decoder **before** the bounded GUI preview. The writer uses the same existing serial bytes and source offsets; there is no independent clone parser and no TX. Full recording explicitly enabled: original stream-XX.bin journals remain authoritative, hashed and immutable; controls.jsonl is a derived, human-auditable index. Live-only Start Monitor remains recording OFF and creates no file.

The on-screen control feed is bounded and drop-oldest, so a busy GUI cannot block recording. Writer-side command indexing continues even if the UI misses frames. A verified offline raw replay re-derives the typed sequence. The UI does not claim absence/timeouts from a partial preview.

**Not yet complete CMD-01:** feedback target-state proof needs a user-approved mapping from command IOA to independent process status IOA, with actual value and quality verification; no inferred breaker motion is presented. IEC103/104 sniff Inspector parity and real multi-vendor FAT remain separate.

## R2 selected .ariec open

The selected-capture ZIP loader streams frames.jsonl in 64-KiB buffers, checks the maximum total uncompressed bytes (1 GiB), maximum JSONL row (128 KiB), maximum manifest (128 KiB), row count, duplicate entries and suspicious compression ratio, and hashes the **entire uncompressed byte stream** before returning any row to the UI. Work happens off the UI dispatcher. Original archive bytes are never changed.

The UI retains only the last 600 preview frames (custom code limit ≤5000) while preserving the total verified row count and integrity indicator. This is an explicit **bounded preview**, not a claim to display every archived frame. Legacy ZIPs missing the expected SHA-256 remain read-only accessible, prominently **LEGACY · hash not available**, and must never be reported as verified. A recorded full passive manifest is still opened by the separate pre-existing lossless **streaming** raw-journal replay path, not by this ZIP preview reader.

**Operational guardrails:** No automatic recording, no implicit external upload, no new physical COM writes, no new WPF window, no per-frame animation. Negative command findings are source-qualified and must not be fabricated from a link NACK.

## Acceptance

- Master control state: source/link-address identity, COT7 positive/negative, Type44–47 and target echoes.
- Monitor command index: typed valid variable FT1.2, rejection of miswired/invalid/test frames; archive retains >512 control stages even when the frame preview is capped at 512.
- Capture reader: 1800-row hashed ZIP with 25 preview rows; SHA mismatch, legacy unverified, overlong JSONL, duplicate ZIP entries.
- Windows Release, all xUnit and protocol smoke, WPF passive GUI smoke, package and CodeQL. Real 2/4 RX and multi-vendor tests remain an explicit later gate.

Note: a SHA256 value stored in the same ZIP detects changes against the stored manifest but is not an authenticated digital signature of the original producer.
