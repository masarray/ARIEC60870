# R3-A — Context-aware Trace, ACD priority, clipboard and command Event facts

**Owner:** TRACE-01 / EVENT-01 / CMD-01; baseline `f0da3d314c56f894219bd2d3479d22cbb1f682e4`. This is implementation plus architectural contract, **not** a claim that all command lifecycle correlation and FAT are complete.

## User-level acceptance (one physical frame = one Trace row)
- IEC101 FC9 is **No requested data**; it is not link NACK FC1. FC9 plus ACD1 after a Class2 request means Class2 empty **and** Class1 pending. The UI must not infer an RTU fault from that combination.
- IEC101 secondary ACK with ACD1 is link confirmation plus pending Class1 indication; it is not a command acceptance or proof that a data payload was delivered.
- Type100 COT6 TX = Send General Interrogation; COT7 +/negative = GI accepted/rejected; COT10 = GI terminated. COT20..36 data = interrogated data. **GI terminated does not prove mapped IOA coverage.**
- Command types 45..51/58..64 and system-command types 100..107 are projected as one immutable, source-referenced Event fact for each actual valid typed TX/RX frame, including passive observations. Process status events remain separate; simple ACTCON/ACTTERM are never treated as real breaker feedback.
- Clipboard **Copy Selected Frame** reproduces timestamp, direction, title, visible meaning and HEX only. Archive `analysis.md` retains the previous full forensic appendix.
- FUN/INF is for IEC103; IEC101/104 use CA + IOA. Explicit Link address must never be silently substituted for Common Address.
- Post-GI Class2 verification yields to ACD1 Class1 draining. Bounded drain remains protected against infinite polls; investigate persistent ACD instead of assuming RTU failure.

## Next architecture milestones — do not bypass
### R3-B: Typed unified TransactionEvent ledger
Introduce session/source-qualified command transaction IDs (protocol/link-A/B/CA/IOA/type/origin/phase), explicit correlation of TX or passive observed request → ACTCON positive/negative → ACTTERM → independently mapped feedback. Full replay regenerates exactly the same timeline. Return signal negative P/N and COT44..47 distinctly; do NOT invent interlock cause. Source missing/overlap → Unknown/Ambiguous, never accepted. Event Log compact chronological projection plus Controls inspector details. Command issued but not observed on the wire must be marked **LocalIntentOnly** rather than fake TX.
### R3-C: GI lifecycle and coverage
Typed GI transaction keyed by source+CA+QOI+OA; separate 'GI initiated', 'ACTCON observed', 'Class1 drained N frames', 'interrogated data N objects', 'ACTTERM observed', 'coverage verified (if approved expected mapping)'. Suppress duplicate counting of A/B or multi-object. No false 'completed' on any non-GI COT10. Show 'No GI data observed' / 'Unknown' when capture partial.
### R3-D: Evidence QA and measured performance
Fix top EVENT counter to reflect real event facts not every measurement frame; distinguish host timestamp from RTU time; clearly show source and direction without redundant text. Add one trace row per physical frame and one Event per object/stage, bounded virtualization, copy/export parity, 1200bps long soak, DPI 100/125/150 and physical 2/4 COM receive-only FAT. Compare interoperability per mode, not universal certified claims.

## Exact regression vectors
- `10 29 69 00 92 16` (2-octet link address 105): secondary FC9 ACD1 DFC0 = Class2 no data / Class1 pending when correlated to Class2 request.
- TI100 QOI20, COT6/7/10 and NEG7; data TI13 COT20.
- Link ACK ACD1 vs plain ACK.
- CMD Type46 S/E, negative ACTCON, ACTTERM without feedback, source/CA mismatch and Type44..47.
- Source Link address 105 and common address 1 MUST remain independent; command CA105 requires explicit separate authorization/configuration.
- 2/4-RX passive may lack full history: mark unknown rather than fabricated failure.

Synthetic tests must pass before merge; physical field and UI manual FAT are separate.
