# ARIEC60870 v1.8.7 — Readable Protocol Trace Line Monitor

## Fixed

- Protocol Trace line monitor no longer uses `<Run>` bindings.
- Added explicit `ProtocolTraceTitle`, `ProtocolTraceMeaning`, and `ProtocolTraceRaw` fields to `EvidenceRow`.
- Fixed the unreadable RAW-only line monitor rendering.
- Trace rows are now compact three-line mono records:
  1. direction / service / address / signal / value
  2. readable meaning
  3. raw hex evidence
- Reduced row padding and visual blank space.

## Why

The previous line monitor template tried to compose text using multiple WPF `Run` elements. That made the XAML fragile and produced poor visual output. The ViewModel now prepares complete trace strings before rendering, making the ListBox template simple, stable, and readable.
