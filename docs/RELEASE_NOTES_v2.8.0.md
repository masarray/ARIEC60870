# ARIEC60870 v2.8.0 — Priority Evidence Router + Drop Summary Marker

## Added

### Priority evidence router

Backpressure now classifies low-value trace events into buckets before dropping:

- `ack/no-data`
- `background-poll`
- `test/supervisory`
- `other-low-value`

Protected evidence remains protected:

- diagnostics / warnings / errors,
- mapped values,
- process values,
- digital values,
- GI activity,
- command ASDUs,
- ACTCON / ACTTERM.

### Drop summary marker

When low-value compression happens, the UI emits a clear summary diagnostic:

- `ARIEC-UI-DROP-SUMMARY`

This records total/new dropped rows and category breakdown, so trace compression is auditable rather than silent.

### Buffer status detail

Buffer status now shows dropped buckets:

- ack/no-data,
- poll,
- test/supervisory,
- other.

## Why

v2.7.0 made the dispatcher adaptive. v2.8.0 makes backpressure more credible: if routine trace noise is compressed, the app explains what was compressed and keeps critical protocol evidence protected.
