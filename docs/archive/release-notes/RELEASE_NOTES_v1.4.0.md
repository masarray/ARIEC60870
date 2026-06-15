# Release Notes v1.4.0 — IEC-101 / IEC-104 Expansion

This release expands ARIEC60870 from a dedicated IEC-103 relay tester into a practical IEC 60870-5-101 / 103 / 104 field tester cockpit.

## Added

- Protocol selector in the desktop Setup overlay:
  - IEC 60870-5-103 serial protection relay mode.
  - IEC 60870-5-101 serial telecontrol outstation mode.
  - IEC 60870-5-104 TCP/IP client mode.
- IEC-101 clean-room master session with FT1.2 fixed/variable frame use, controlled Class 1 / Class 2 polling, General Interrogation, optional CP56Time2a clock sync, raw evidence, and common ASDU decoding.
- IEC-104 clean-room client session with TCP transport, STARTDT, I-format ASDU send/receive, S-format acknowledgement, U-format connection control, TESTFR health check, General Interrogation, optional CP56Time2a clock sync, and common ASDU decoding.
- Generic IEC-101/104 ASDU builder and decoder for common Type IDs, VSQ, COT, common address, IOA, monitoring values, command ASDUs, interrogation, read command, and clock sync.
- IEC-104 APCI/APDU parser and frame builder.
- Built-in IEC-101 outstation simulator and IEC-104 server simulator so the desktop dashboard can be demonstrated without external hardware.
- Protocol smoke tests for IEC-101 ASDU round-trip and IEC-104 APCI/APDU parsing.

## Notes

- IEC-103 mapping profiles remain FUN/INF-based.
- IEC-101/104 currently show IOA-based decoded evidence. Project-specific IOA naming can be added later as an IOA mapping profile.
- IEC-104 security/TLS is not implemented in this clean-room desktop build.
