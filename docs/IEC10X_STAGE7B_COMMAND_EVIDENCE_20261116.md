# Stage 7B — command application evidence and logical feedback (2026-11-16)

## Scope and root causes
- Prior `Iec10xCommandCasebook` compared command DCS/SCS raw numeric values to RTU SPI/DPI numeric codes. For example **DCS=1 means OFF** but **SPI=1 means ON**: a mapped single-point ON could incorrectly mark an OPEN/OFF double command as matched.
- Casebook relied on readable `ValueMeaning` and S/E phase while ignoring QU/QL differences in application response echoes. A wrong ACTCON qualifier could therefore be treated as positive acceptance.
- One mapped feedback IOA could satisfy multiple concurrent command cases. IEC-101 feedback lacking an explicit matching link address could satisfy a known-link command. COT9 was interpreted as cancellation without an observed COT8.
- The Stage 7A runtime SBO tracker did not revoke accepted SELECT immediately when a COT8 deactivation TX was recorded.

## Design
The shared `Iec10xCommandWireIdentity` is reused by the IEC-101 live SBO gate and IEC-101/104 evidence casebook. It takes only canonical decoder bytes, validates one decoded command object and normalizes **only** the S/E bit. Identical originator, TI, CA, IOA, value, qualifier and timed elements are required. No additional protocol parser.

Casebook preserves bounded 128 pending / 240 history / 24 stage caps:
1. `ExpectedLogicalOn` maps C_SC SCS 0/1 and C_DC DCS 1/2 to true/false process-state intent. Actual mapped SPI and DPI values are independently decoded to true/false; invalid DPI 0/3 stays **indeterminate**, never positive proof.
2. Source, CA and explicit IEC-101 link must match feedback. One qualifying feedback telegram mapping to multiple active commands makes those cases ambiguous; it does not resolve both positively.
3. COT9 can close a case as cancelled only after a matching observed Master COT8. Unsolicited COT9 remains incomplete evidence. COT8 with altered target marks ambiguity.
4. A matching positive EXECUTE ACTCON and ACTTERM are still distinct stages. A mismatch before ACTTERM persists in the operator verdict when no later correct feedback arrives.
5. Physical COT8 TX revokes the live IEC-101 SBO select authority immediately. A positive SELECT response alone still does not prove equipment operation.

## Deterministic verification
- C_DC OPEN DCS=1 vs SPI=1 ON is a mismatch; SPI=0 OFF matches.
- C_DC CLOSE DCS=2 matches SPI=1 or DPI=2.
- C_SC OFF/ON matches DPI=1/2.
- DPI=0/3 never matches a digital command target.
- Different QU with same value is ambiguous; no ACTCON success.
- Same feedback IOA for two pending commands is ambiguous, not two successes.
- COT9 without COT8 cannot close a command case; COT8 cancellation closes after exact DEACTCON.
- Missing link identity cannot authorize IEC-101 feedback.
- SBO selected-then-cancelled cannot EXECUTE without a fresh positive SELECT ACTCON.
- Existing deterministic GI/FCB/recovery/WPF suites remain mandatory.

## Boundaries
These changes do not assert independent physical switchgear travel; mapped RTU feedback is process-data evidence only. Real SICAM CP8050/CI8551 and serial-server FAT at 1200 bps, event parity at 60/500/2000 SOE, long-run duplicate and reconnect reliability, and vendor-specific timings remain unverified. Do not publish as certified before physical evidence.
