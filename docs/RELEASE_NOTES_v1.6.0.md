# Release Notes v1.6.0 — Forensic Profile Engine Pass

This release is a protocol-depth rebuild for IEC 60870-5-101 and IEC 60870-5-104 usability. It focuses on making the app usable by field engineers who need readable values, clear quality flags, and an interoperability profile that matches the slave/server under test.

## Added

- Interoperability profile controls in the setup overlay:
  - COT size: 1 or 2 octets.
  - Common Address size: 1 or 2 octets.
  - Information Object Address size: 1, 2, or 3 octets.
  - IEC-101 link-address size: 1 or 2 octets.
  - IEC-101 balanced/unbalanced mode marker.
  - IEC-104 t1/t2/t3/k/w fields for the runtime profile.
- IEC-101 FT1.2 fixed/variable frame support for 2-octet link addresses.
- Multi-object ASDU decoding for IEC-101/104, including SQ=0 and SQ=1 addressing.
- COT flag decoding:
  - Test flag.
  - Positive/negative confirmation flag.
  - Originator address when configured.
- Quality decoding separated from engineering value:
  - Good.
  - Invalid.
  - Not topical.
  - Substituted.
  - Blocked.
  - Overflow where applicable.
- CP56Time2a decode for time-tagged IEC-101/104 information objects.
- IEC-104 basic forensic checks:
  - STARTDT confirmation missing.
  - TESTFR confirmation missing.
  - N(S) sequence discontinuity.
  - N(R) acknowledgement of unsent I-frame.
- Findings for negative COT and non-good quality flags.
- Smoke tests for two-octet IEC-101 link address and multi-object IEC-101/104 value/quality decode.

## Changed

- Value Viewer now separates `Value / state` from `Quality / flags`; QDS is no longer mixed into the value text for IEC-101/104 measurements.
- IEC-101/104 rows now use IOA/CA/COT/Type ID language, while IEC-103 keeps FUN/INF language.
- IEC-104 TESTFR interval is profile-driven by t3 instead of a hardcoded 15 seconds.
- Product metadata now identifies the app as ARIEC60870 Protocol Lab.

## Why this matters

Before this pass, the application could connect and decode basic frames, but it was still too easy to misread a slave/server because COT size, CA size, IOA size, link-address size, quality flags, and multi-object ASDUs were not treated as first-class forensic evidence.

This release moves the app closer to a practical protocol forensic workflow: configure the interoperability profile, capture raw frames, decode all objects, separate value from quality, and raise findings when slave/server behaviour is suspicious.

## Known limitations

- IEC-101 balanced mode is currently represented in the profile UI but the active polling engine remains master/client oriented.
- IEC-104 k/w/t1/t2 are visible in the profile and t3 is used for TESTFR cadence; full k/w/t1/t2 enforcement remains a next pass.
- Command lifecycle validation is still planned: direct operate, select-before-operate, ACTCON/ACTTERM, and negative command tests.
- Full forensic evidence package hashing is planned for a later release.
