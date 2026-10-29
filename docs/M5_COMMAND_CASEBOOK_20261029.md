# M5 — Source-qualified Command Casebook

Implementation checkpoint for PRD CMD-01 / CAPTURE-01 / FINDING-01. Not field-certified.

## End-to-end operator workflow

1. IEC101/104 active or IEC101 passive RX-only: open Controls beside Trace and Findings. The workspace never transmits.
2. One bounded case records Master SELECT/EXECUTE, RTU positive/negative ACTCON, ACTTERM, DEACTCON, ambiguity and source-matched process feedback.
3. Select a case to inspect evidence stages and source references: link/source, CA/IOA/Type, COT, direction, host timestamp, exact frame sequence. Open Source Frame jumps to Trace only if exact source/sequence is still in its bounded preview.
4. Copy Case for Analysis exports stage text, original raw HEX and provenance to the clipboard, no cloud upload.
5. Reopen verified selected-frame .ariec ZIP to reconstruct cases from typed frames; a partial excerpt with missing stages is Observed incomplete, never a proven RTU timeout.
6. PDF and Markdown report summary carries command-case counts, observed attention cases and caveats. ACTCON or ACTTERM alone never proves equipment travel.

## Architecture and safety

- One bounded application-case history owned by ARIEC60870.Master/Protocol/Iec10x/Iec10xCommandCasebook.cs, consuming the pre-existing typed decoder model. No second ASDU parser, no extra TX.
- Replaces the old duplicated WPF command ledger and its fixed 8-second timeout. Pending limit 128, case history 240, per-case stage history 24; original captured bytes remain authoritative.
- Keyed by protocol, physical source, IEC101 link address, CA, IOA, Type and originator. Repeated/overlapping requests mark ambiguity instead of silently overwriting; wrong-source replies remain unmatched.
- Trusted COT P/N/TEST, checksum and ASDU issues prevent false positive. Response matching includes SELECT/EXECUTE echo and expected value.
- Only engineer-mapped feedback IOA, matching source/CA, decoded SP/DP state matching the requested target and good SIQ/DIQ quality can establish Feedback matched. This means RTU-reported state, not physical switchgear proof.
- COT44–47 and negative P/N indicate application rejection, not proof of which interlock or hardware state caused it.
- IEC103 controls, balanced IEC101, IEC104 cross-reconnect session IDs and long-run multiday case-id archive indexing remain future proof gates.

## Deterministic tests and CI acceptance

- Both ACTTERM/feedback arrival orders; negative P/N; COT44..47; wrong source; overlapping commands; echoed wrong target; no mapped feedback; bad quality; partial archive; reset and bounded history.
- Windows Release, protocol smoke, xUnit, GUI package smoke, CodeQL and hygiene before merge.
- Physical serial FAT, Win DPI verification and long monitoring are separate; do not certify interoperability based only on CI.