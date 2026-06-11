# Product Redesign Audit — ARIEC60870 Protocol Lab

This build moves the desktop UX from an IEC-103-first tester to a protocol-aware IEC 60870 analyzer.

## Locked product direction

The product is now treated as an ARIEC60870 protocol lab with three native profiles:

- IEC 60870-5-103 protection relay serial profile.
- IEC 60870-5-101 telecontrol serial profile.
- IEC 60870-5-104 telecontrol TCP/IP profile.

## Audit findings fixed in this pass

- Connection setup no longer presents a mixed 103/101/104 parameter wall.
- IEC-104 hides COM/baudrate/class polling setup and shows TCP/APCI-oriented information.
- IEC-101 uses CA/IOA/Type ID/COT language instead of pretending FUN/INF is universal.
- IEC-103 keeps FUN/INF and mapping profile workflow because that is the natural protection-relay addressing model.
- Operator Evidence, Frame Trace, Value Viewer, and Event Log now expose protocol-specific columns.
- IEC-104 Frame Trace now shows APCI format and sequence columns instead of serial Class columns.

## Protocol UX rules

### IEC-103

Primary UI language: Class 1/Class 2, ACD/DFC, ASDU, COT, FUN, INF, relay timestamp, mapping profile.

### IEC-101

Primary UI language: FT1.2 serial frame, link address, Type ID, VSQ, COT, common address, IOA, value, quality, time tag.

### IEC-104

Primary UI language: TCP/IP endpoint, APDU, APCI, I/S/U format, N(S), N(R), STARTDT/TESTFR control, Type ID, COT, common address, IOA.

## Remaining protocol-depth roadmap

- IEC-104 k/w/t1/t2/t3 fields are now visible; t3 drives TESTFR cadence. Full k/w/t1/t2 enforcement is still a later pass.
- Add IOA naming profile for IEC-101/104.
- IEC-101 balanced/unbalanced is now visible as profile metadata; active balanced-mode procedure remains a later pass.
- Quality descriptor decoding is now separated from value text for IEC-101/104 common value types.
- Basic IEC-104 sequence/window findings are now present; deeper k/w state-machine validation remains a later pass.


## v1.6.0 forensic audit pass

The app now treats interoperability settings as part of the evidence contract, not a hidden assumption. COT size, CA size, IOA size, IEC-101 link-address size, quality flags, COT P/N and test flags, CP56Time2a timestamps, and multi-object ASDUs are decoded into separate fields so values are readable and audit findings can point to protocol-profile mismatch.
