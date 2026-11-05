# P0-C2 — IEC-104 APCI conformance and session-owned TCP reassembly

**Base:** master bef7672228652cadda29acbe582a9b1ba8bb7855 (P0-C1 merged). **Scope:** IEC-104 receive integrity and deterministic tests. This is a partial P0-C milestone, not universal conformance certification.

## Root cause

The Core APDU parser previously marked I/S/U envelopes valid using only the first control octet and declared length equality. It could accept an empty I-format ASDU, nonzero reserved S/U control octets, S/U payloads, odd receive-sequence reserved bits, or undefined combinations of U operations. Separately, the forensic evidence verifier used tighter but independent APCI rules. Live session acceptance and archived byte verification could therefore disagree on the same raw bytes.

The IEC-104 TCP reader was recreated on every ReceiveOneAsync operation. A read timeout while only part of an APDU had arrived discarded those bytes; the next call scanned from the middle for 0x68, which could misframe remaining data. This was a receive-lifecycle ownership defect rather than a reason to add polls, GI or retries.

## Correct architectural boundary

- Core Iec104ApciWireLayout validates all IEC-104 APDU structural invariants: 0x68, 4..253 octets of declared payload, declared versus actual length, I-format nonempty ASDU and even N(R), S-format exact control field, six valid U-format functions only and all U reserved octets zero. The parser and forensic APCI verifier now share this one structural authority.
- The existing Iec104ApduParser retains malformed frame bytes, format classification and issues for diagnostics; IsValid gates active Master sequence state. Application/type interpretation stays with the existing Iec10xAsduDecoder. Unknown vendor-private ASDU semantics are not guessed by the structural validator.
- One bounded Iec104StreamReader belongs to the TCP session, not each individual read call. It retains incomplete bytes across ordinary receive timeouts, consumes no more bytes than the current APDU needs, discards leading non-0x68 noise and returns impossible-length prefixes as untrusted malformed evidence. No background read-ahead, extra TX, GI, supervision changes or unbounded queue.
- The session rejects invalid STARTDT/TESTFR evidence; invalid U-frame confirmations do not inflate ACK counters, and malformed I-frames do not trigger S-ACK. Valid out-of-sequence but structurally conforming I-frames may still be answered with the last contiguous N(R). A valid link ACK is not evidence of control-command success.

## Automated acceptance added

- Core: negative malformed I/S/U matrix, six valid U cases, 15-bit sequence maximum, valid IEC-101/104 common ASDU, 50k mixed valid/invalid structural cases with runner-local elapsed time and allocation diagnostics.
- Master: receive timeout between APDU bytes then exact completion; coalesced next frame preserved; invalid length followed by valid APDU; 5k interleaved U frames and leading noise; live/archived APCI parity; malformed STARTDT con cannot enable I-format TX.
- Preserve P0-A and P0-B tests: 101/103/104 decoding parity, 2,560 IEC101 SOE objects beyond WPF preview, 50k FT1.2 framer, passive strictly RX-only, existing Windows WPF checks.

## Verification and open gates

This branch is a candidate until exact-HEAD Windows Release, all xUnit and protocol smoke, CodeQL, repository/dependency checks and WPF smoke are proven. Test timing diagnostics on shared runners are not fixed absolute performance limits.

Still open: IEC104 TCP connection reset/epoch stress, full 50k mixed event pipeline/GUI p95 latency and memory plateau, independently validated IEC103 disturbance sequences, hardware IEC101 1200bps 2/4 COM and IEC104 multi-vendor interoperability. No conclusion about external RTU FIFO overflow can follow from a successful CI run.

Independent normative reference used for APDU structural limits: IEC 60870-5-104:2000/2006, defined APDU control field and 253-octet maximum; no external software source code copied.
