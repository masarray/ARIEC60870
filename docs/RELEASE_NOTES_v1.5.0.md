# Release Notes v1.5.0 — ARIEC60870 Protocol Lab Redesign

## Product repositioning

The desktop app is redesigned from an IEC-103-first tester into a protocol-aware IEC 60870 analyzer for IEC-103, IEC-101, and IEC-104.

## UX changes

- Renamed the desktop shell to **ARIEC60870 Protocol Lab**.
- Added protocol-aware setup profiles.
- Added protocol-specific evidence columns.
- IEC-103 keeps FUN/INF-centric views.
- IEC-101 uses Type ID, COT, CA, IOA, quality, and FT1.2 serial evidence.
- IEC-104 uses APCI format, N(S), N(R), Type ID, COT, CA, IOA, and APDU-oriented evidence.
- Hidden irrelevant setup fields per protocol so users do not see COM/baudrate on IEC-104 or APCI terms on IEC-103.

## Engineering changes

- Added protocol-aware metadata to master evidence events.
- Added protocol-aware value and event row models.
- Added IEC-104 APDU and IEC-101/104 ASDU fields to UI binding models.
- Updated selected-frame protocol map behavior so IEC-104 no longer appears as an FT1.2 serial telegram.

## Known limits

- IEC-101/104 IOA naming profile is reserved for a later pass.
- IEC-104 TESTFR is present as a fixed health check, but k/w/t timer controls are not yet editable settings.
- Build validation still needs to be run in a Windows/.NET SDK environment.
