# ARIEC60870 v1.7.1 — Event Log Smart Change + Value Viewer Grouping Pass

This pass fixes several field-usability defects found during IEC-101 demo validation.

## Fixed

- Event Log is now treated as a change journal, not a value mirror.
  - IEC-101/104 digital events are logged only when a trustworthy before/after transition exists.
  - OFF→OFF and ON→ON are suppressed.
  - The desktop layer now prefers its last known runtime value cache over inconsistent engine-provided previous values.
- Value Viewer now groups digital/protection status points above analog/measurand points.
- Value Viewer ordering is based on IOA address within each group, matching commissioning/database review habits.
- Changed Value Viewer rows get a lightweight row highlight for five seconds.
- Header counter chips are wider fixed-width pills so TX/RX and GI/C1/C2 LEDs stay visible when counters grow.
- The side product logo now changes by selected protocol:
  - IEC-101: green IEC 101 icon.
  - IEC-103: blue IEC 103 icon.
  - IEC-104: orange IEC 104 icon.
- Protocol icons are embedded as WPF resources.

## Notes

PC arrival time remains hidden from operator-facing grids. The application shows IED/RTU timestamps when supplied by the device and displays `no timestamp` when the device did not send a timestamp.

## Validation performed in sandbox

- XAML parse: OK
- Resource/project XML parse: OK
- C# brace balance: OK
- ZIP integrity: OK

Full .NET build must still be run on a Windows machine with .NET SDK / Visual Studio.
