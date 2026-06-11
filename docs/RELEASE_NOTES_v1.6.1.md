# Release Notes v1.6.1 — Serial Field-Rate Profile Fix

This maintenance release fixes an important field-usability gap in the IEC-101/103 serial setup.

## Fixed

- Added low serial baudrates to the desktop setup: 300, 600, **1200**, 2400, and 4800 bps.
- Kept common bench/IED rates available: 9600, 19200, 38400, 57600, and 115200 bps.
- Made the baudrate combo box editable so project-specific serial rates can be entered directly.
- Added a low-baud serial timing guard: at 1200 bps and below, response timeout, Class 2 polling interval, busy backoff, and timeout-recovery backoff are widened to avoid false failures caused by low-speed channel timing.
- Preserved 9600 bps as the default selection to avoid surprising existing bench users.

## Why this matters

IEC-101/103 serial links are field-profile driven. Low-rate channels such as 1200 bps are still used in legacy utility/RTU environments, so the analyzer must not assume only 9600/19200 bps operation.

## Validation focus

- IEC-101 at 1200 bps with larger response timeout.
- IEC-101 Class 1/Class 2 polling over low-speed serial.
- IEC-103 regression at 9600/19200 bps.
