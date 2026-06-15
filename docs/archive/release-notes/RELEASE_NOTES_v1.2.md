# ARIEC60870 v1.2 — Stop-Safe UI + AR Software Identity

## Fixed

- Fixed a desktop runtime issue where pressing Stop during an active serial/read-write operation could leave both Start and Stop disabled.
- Stop now cancels the session and requests active transport close to release blocking serial read/write operations.
- Stop remains available as a force-close action while shutdown is in progress.
- Serial transport read path now uses timeout-aware `SerialPort.Read` inside a controlled task instead of relying only on `BaseStream.ReadAsync` cancellation.

## Improved

- WPF theme aligned with the mature engineering software identity.
- Cards, buttons, segmented tabs, DataGrid rows, focus behavior, and scrollbars were refreshed for a cleaner engineering cockpit.
- Landing page redesigned with modern engineering cockpit-style hero, glass session panel, architecture map, field-truth section, and restrained typography.

## Product Rules Preserved

- Active IEC-103 master tester remains the primary product direction.
- Operator Evidence / Value Viewer / Relay Event Log remain the main user-facing output.
- Frame Trace remains the advanced raw protocol transparency layer.
- Signal names still come only from user mapping profiles.
- Class 2 remains the normal polling cycle; Class 1 remains ACD-driven or bounded GI follow-up only.
