# ARIEC60870 v3.6.1 — Smart Trigger Capture Dashboard

## Added

### Trigger Captures workspace

A new `Trigger Captures` workspace lists automatic IEC trigger evidence artifacts.

Each saved trigger capture shows:

- completion time,
- severity,
- trigger code,
- row count,
- trigger row sequence,
- trigger title,
- saved `.ariec` file path.

### Trigger capture details

Selecting a trigger capture row displays readable detail:

- capture ID,
- severity,
- trigger code,
- title,
- row count,
- trigger row,
- file path,
- trigger detail.

### Trigger artifact actions

The workspace includes:

- `Copy Path`
- `Open Folder`

This makes automatic trigger captures discoverable and reviewable, instead of silently saving files in the background.

## Improved

### ASE-style trigger workflow direction

Trigger capture is now a visible workflow:

1. protocol trigger is detected,
2. pre/post `.ariec` capture is saved,
3. dashboard row is added,
4. user can copy/open/review the capture artifact.

## Preserved

- IEC trigger watch and pre/post capture engine.
- Protocol Trace and Evidence Summary multi-select.
- Unified `.ariec` evidence capture.
- Left-rail Auto Scroll Latest.
- Clickable panel header collapse UX.
