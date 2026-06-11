# v1.6.7 — PLN PUSERTIF Form Seed Profile Pass

This build replaces the earlier generic PLN/Pusertif placeholder with a real editable seed profile derived from the uploaded PLN PUSERTIF gateway communication test form for IEC 60870-5-101/104 to IEC 61850.

## What changed

- Rebuilt `profiles/PLN_Pusertif_IEC101_default_seed.json` from the PUSERTIF form.
- Added 27 default points covering:
  - TSS single-point status with CP56Time2a.
  - TSD double-point status with CP56Time2a.
  - TM telemetering float/scaled/normalized values.
  - RCD double command points with feedback IOA references.
  - CTC regulating step command for tap changer.
  - RCA normalized setpoint command.
  - Link fault, IED faulty, MPU trip/status points.
- Added default interoperability profile values:
  - Link Address size: 2 octets.
  - CAASDU size: 2 octets.
  - IOA size: 3 octets.
  - COT size: 2 octets.
  - Speed: 1200 bps.
  - Serial: 8E1.
  - CAASDU: 105.
  - IEC-104 IP hint: 172.21.1.35.
  - IEC-101 serial hint: COM21/22.
- Added PUSERTIF-style test scenario metadata:
  - TSS.
  - Monitoring Link Komunikasi.
  - TSD.
  - Telemetering.
  - Remote Control Digital.
  - Setpoint RCA.
  - Control Tap Changer.
  - SOE.
  - Time Synchronization.
  - Pengujian Fitur Komunikasi.
- Added command support for:
  - `C_RC_NA_1` regulating step command.
  - `C_SE_NA_1` normalized setpoint command.
- The Command Dock now exposes Single, Double, Regulating, and Setpoint Normalized command types.
- When a 101/104 IOA profile contains default settings, the setup screen can apply CA, COT/CA/IOA length, baudrate, serial mode, link address size, and TCP host hints.

## Safety note

This profile is a seed database extracted from a test form. Always verify it against the active project I/O list, official interoperability sheet, and safe test boundary before issuing commands to real equipment.
