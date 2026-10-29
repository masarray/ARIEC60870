# IEC-101 passive analyzer — short-response interoperability regression

## Verified protocol behavior
IEC 60870-5-101:1995/AMD2:2001, subclause 6.2.1.1 / Table 10:
- One-byte `E5` is a permitted short response in place of a fixed secondary **FC0 ACK** for supported confirm requests **or** secondary **FC9 requested-data-not-available** after a Class 1/Class 2 poll, provided the short response need not communicate ACD=1 or DFC=1.
- `E5` contains **no link address, link-function code, ACD or DFC bits**. Correlate only against an unambiguous outstanding compatible request and label inferred interpretation explicitly; never put a synthesized function code in raw fields.
- Link-status request (primary FC9) expects secondary FC11 (or defined error cases); a lone E5 must not satisfy that service.
- One-byte `A2` **must not be used** under IEC-101. Preserve byte evidence, show an unsupported single-character warning and **never treat A2 as a valid NACK confirming a transaction**.
- Fixed ACK uses format `10 C A CS 16` where address length is 0/1/2 octets according to the active IEC-101 interoperability profile. On a 2-octet circuit, configuring link width 1 causes misleading parsing/checksum/ASDU diagnostics.

## Proven code issue corrected here
Previous P2B had hardcoded assertions that `E5` is always ACK and is never no-data, while treating `A2` as a legal IEC-101 NACK. This contradicted the companion standard and could generate misleading diagnosis in a real master/RTU interoperability dispute.

The correction is in the pure Monitor correlator, GUI evidence labels and focused regression tests. No serial TX, active master behavior or physical hardware settings are modified. The lower-level scanner still retains the raw A2 octet as a single character for faithful forensic evidence; only protocol semantics reject it.

## Capture acceptance scenario: troubleshoot master/RTU switching
1. Record full master-TX and RTU-TX streams without modifying the production link. Save verified raw journals and decoder widths.
2. Compare same request with a subsequent short E5 vs a fixed secondary FC0/FC9 reply. Display full raw bytes and addresses for fixed reply, and explicitly `address absent / function inferred` for E5.
3. Verify link-status FC9→FC11, Class1/2 FC10/FC11→FC8/FC9 or a context-permitted E5; detect unmatched/retry/DFC/timeouts without treating ordinary standby link-status traffic as a fault.
4. Show `Unknown Function` requests with actual primary FC and whether there was a valid secondary FC15 or genuine silence; no-response policy is a deployment interoperability choice, not automatic success or failure.
5. Detect GI **Type ID 100** activation (COT6) and distinguish startup GI from GI following an independently observed/identified switchover. A 2-COM sniffer sees one duplex link, so it cannot prove opposite A/B activity.
6. Preserve host timestamp uncertainty. 33-bit idle behavior cannot be proven from Windows USB serial *read-completion timestamps*; require a bit-accurate analyzer/hardware line monitor to establish inter-frame electrical idle.
7. Compare identical recordings and decoder output before/after an interoperability change. Report cause only when observable frames and timing support it.

## Reference documents (independent summary, no vendor code/assets)
- IEC 60870-5-101:1995 Amendment 2:2001, section 6.2.1.1, Table 10: https://cdn.standards.iteh.ai/samples/9463/ec043d834e884211a2edc6ef9bfed5bc/IEC-60870-5-101-1995-AMD2-2001.pdf
- independent IEC-101 reference User Manual, IEC 60870-5-101 Interoperability Options / Short Ack Class Response: independent protocol reference

### Follow-on engineering needs
- Link-width detect/override in *offline replay*, with confidence measure; never silently change stored raw capture, and preserve original manifest profile as captured.
- Dedicated status table: physical bytes vs decoded function vs inferred function, pre/post GI mode, unknown-function FC and whether a response truly occurred.
- WPF frame-inspector correlation deep link, search/index over full raw journals rather than only latest bounded GUI rows, side-by-side capture diff and test evidence export.
- 2-COM serial capture is not equivalent to proprietary electrical timing analyzers. Four RX inputs needed for simultaneous redundant A/B circuits.
