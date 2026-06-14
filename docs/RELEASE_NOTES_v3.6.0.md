# ARIEC60870 v3.6.0 — IEC Trigger Watch + Pre/Post Evidence Capture Foundation

## Added

### IEC protocol trigger watch

ARIEC now observes important IEC protocol events and automatically starts a pre/post evidence capture window.

Built-in trigger classes:

- IEC common address mismatch
- IEC negative confirmation / NACK
- IEC timeout / communication error
- IEC DFC/busy condition
- IEC ACD/access demand
- IEC General Interrogation milestone
- IEC command lifecycle evidence
- IEC digital event / spontaneous change
- IEC quality issue

### Pre/post trigger capture

When a trigger is detected, ARIEC collects:

- pre-trigger evidence rows,
- the trigger row,
- post-trigger evidence rows.

Completed trigger windows are saved as `.ariec` capture files in the local trigger evidence folder.

### Trigger diagnostics

Trigger activity is written into Diagnostics with markers:

- `ARIEC-IEC-TRIGGER-STARTED`
- `ARIEC-IEC-TRIGGER-CAPTURE-SAVED`
- `ARIEC-IEC-TRIGGER-CAPTURE-FAILED`
- `ARIEC-TRIGGER-CAPTURE-SKIPPED`

### Trigger metadata in capture retention

Capture retention metadata now includes trigger statistics:

- started trigger count,
- completed trigger count,
- active trigger windows,
- configured pre/post rows.

## Why this matters

This closes one of the most important gaps versus mature protocol analyzers: the tool can now capture important IEC protocol moments automatically, instead of relying only on manual selection after the event.

## Preserved

- Professional Protocol Trace UX.
- Evidence Summary multi-select.
- Unified `.ariec` capture.
- Left-rail Auto Scroll Latest.
- Clickable panel header collapse UX.
