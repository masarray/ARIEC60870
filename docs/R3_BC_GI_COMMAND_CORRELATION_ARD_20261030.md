# R3-B/C — Source-qualified GI conversation evidence and command safety
**Date:** 2026-10-30. **Authority:** companion to `IEC10X_INTEROPERABILITY_PRD_20261029.md`, `IEC10X_INTEROPERABILITY_ARD_20261029.md`, `R3_TRACE_AND_COMMAND_FORENSICS_ARD_20261030.md`. Predecessor R3-A: PR #59 merged. This document defines implementation truth only for features actually merged.

## Standard-grounded engineering decisions
- IEC-101 secondary ACD=1 signals pending Class 1 access demand, independent of Class 2 FC9 NO DATA. A Class 1 request is a link-layer poll, not an ASDU GI result.
- IEC-10x Type100/QOI20 is station GI; QOI21..36 are group interrogation. COT6 activation, COT7 positive/negative confirmation, COT10 termination. Interrogated process data uses COT20..36 (distinguish spontaneous COT3).
- IEC-101 unbalanced Class1/2 are link delivery classes; IEC-104 does not expose a Class1 poll transport. Do not invent Class1 in IEC-104.
- ACTTERM ends an observed application sequence but **does not prove process value coverage**. Percentage is forbidden unless an approved expected-IOA mapping and a sufficiently complete observation source exist.
- Historical Norwegian User Conventions are opt-in profiles. Not universal assumptions; clean-room code (no vendor table pasted).

## Typed GI casebook — Master owns semantics
One **source-qualified** case: Protocol, source A/B/single, optional IEC101 link address, CA, originator, QOI, request evidence identity. Read-only observer; no active write. Track request, ACTCON positive/negative, independently observed interrogated data frame count, object count (all SQ1/SQ0), unique IOAs, bad-quality count, and ACTTERM; retained first-data/termination source references. Overlapping requests -> Ambiguous, never last-wins. Unmatched RTU reply -> Unmatched rather than fabricated request. Bounded unique-IOA/seen-frame/history. Capture end -> Observed incomplete, never RTU timeout from missing file tail.

GI Events: only milestone rows (request/ACTCON/ACTTERM) plus a meaningful summary at termination; do not emit one SOE row per GI snapshot. Use host-labeled time for untimed GI event; preserve original raw and provenance. Existing Value Viewer receives all decoded process objects. Event header count means actual Event Log facts, not all measurement frames.

Command hardening: use source-matched actual case stage for diagnostics, not newest case by insertion time; do not match feedback from a different IEC101 link when explicit link is available; reject malformed and TEST evidence. Commands are not transmitted in monitor/replay.

## Performance and tests
Single authoritative ASDU decoder; `Iec10xGiCasebook` in Master, WPF projection only. Active capacity 64, unique IOA budget 2048 per GI, total per-case seen-frame budget 8192; overflow truthfully marks counting limited. No WPF per-frame redraw. Test SQ1, source/CA/link mismatch, unobserved request, negative ACTCON, overlap, group GIs and invalid quality; test regression whole repo Windows release once per final HEAD. Physical 1200-baud and 2/4-RX FAT separate and not claimed complete.

## R3-B/C remaining
Full source-scoped command correlation including OA, response phase, approved mapping feedback and case navigation is present in `Iec10xCommandCasebook`, but automation-grade proof remains subject to explicit configuration and physical FAT. Implement exact event→case deep links, a dedicated GI details view or inspector, source-verified coverage percentage only after approved mapping, capture-roundtrip parity and more long-soak evidence in #60. Do not mark these complete from synthetic tests.
