# ARIEC60870 Core — Protocol Parsing Contract

These instructions apply to `src/ARIEC60870.Core/**` and extend the repository root `AGENTS.md`.

## 1. Core authority

Core owns byte-level FT1.2/link/ASDU decoding, protocol primitives, mapping primitives, and offline analysis semantics. UI and Master may consume Core results; they must not duplicate parser truth.

Parsing is evidence-preserving protocol work. Malformed input must be observable and bounded, never converted into invented protocol meaning.

## 2. Root-cause-first parser workflow

For decoding defects:

1. reduce the failing frame to the smallest lawful synthetic fixture that reproduces the issue;
2. identify the exact protocol layer/field whose invariant is violated;
3. fix the parser/model at that layer;
4. add deterministic positive + negative regression coverage;
5. verify existing valid frames remain unchanged.

If three patches still chase parsing symptoms, STOP before patch four and re-audit framing boundaries, length rules, field offsets, type/COT interpretation, and parser ownership.

## 3. Try/Result parsing, not exception control flow

Untrusted frame bytes must not use exceptions for expected malformed-data control flow.

Prefer explicit parse outcomes such as `TryParse`, typed Result objects, or equivalent discriminated status carrying:

- success/failure;
- failure code/layer;
- safe offset/field context;
- parsed value when valid;
- raw evidence needed by higher layers.

Exceptions remain appropriate for genuine programming/invariant bugs, not normal invalid field values, short frames, unsupported ASDU types, checksum mismatch, or truncated input.

## 4. Defensive byte parsing

Before every indexed/length-dependent read validate the required bounds.

Explicitly handle where applicable:

- minimum frame length;
- declared vs available length;
- checksum/end byte;
- control/link fields;
- ASDU header availability;
- FUN/INF/data payload boundaries;
- time fields;
- unsupported/unknown Type or COT;
- numeric conversion and overflow;
- partial captures/noise before a frame.

Never trust a length byte enough to index memory without checking actual buffer bounds.

## 5. Preserve unknown evidence

Unknown/unsupported values must remain visible as unknown/raw evidence. Do not guess vendor semantics, signal names, or unsupported protocol meaning.

Friendly interpretation is allowed only when supported by protocol data or user mapping. Raw bytes and numeric protocol identity remain available to callers.

## 6. Bounded work

Parser complexity must be bounded by supplied data. Do not add unbounded search/retry loops for malformed streams.

For stream resynchronization, make progress explicit: every iteration must consume input, find a bounded candidate, or terminate with a structured result.

Avoid unnecessary per-frame allocations on high-frequency paths. Pool/reuse only when measurement justifies the complexity.

## 7. Numerical/time correctness

Validate protocol time/date fields before constructing host date/time values. Invalid relay timestamps must remain invalid/diagnostic evidence rather than silently becoming PC arrival time.

Numeric decoding must define byte order, signedness, scale/representation, and invalid/overflow behavior explicitly.

## 8. Diagnostics separation

Core should return compact structured parse evidence/errors. It should not synchronously format large UI/report strings on hot parsing paths.

Higher layers may turn structured results into operator diagnostics. Error storms from repeated malformed/noise input must be bounded/aggregated by the owning runtime layer.

## 9. Regression contract

Parser changes should cover applicable cases:

- valid fixed/variable frame;
- shortest valid frame;
- truncated frame at each meaningful boundary;
- declared-length mismatch;
- checksum/end failure;
- unknown Type/COT/FUN/INF;
- invalid timestamp;
- boundary numeric values;
- back-to-back frames/noise/resynchronization;
- round-trip/golden bytes where the project owns lawful fixtures.

Do not change expected bytes/interpretation in golden tests merely to make a new implementation pass without explaining why the old expectation was wrong.

## 10. Completion evidence

For substantial Core changes report root cause, protocol layer, structured failure behavior, regression fixtures/tests, performance/allocation impact where relevant, and any unsupported ambiguity that remains.

## Final rule

One malformed or unknown frame must never crash the analyzer, hang the stream parser, erase raw evidence, or force higher layers to invent meaning.
