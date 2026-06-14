# ARIEC60870 v3.4.8 — Unified Evidence Capture + Evidence Summary Multi-Select

## Added

### Evidence Summary multi-select

Evidence Summary now supports the same evidence selection workflow as Protocol Trace:

- click row,
- Shift-click range,
- Ctrl-click toggle,
- click-drag top-to-bottom or bottom-to-top,
- right-click row selection.

### Evidence Summary context menu

Evidence Summary now has a right-click evidence workflow:

- `Export Selected Capture File...`
- `Export Selected Evidence Text...`
- `Select All Visible Evidence Rows`
- `Clear Selection / Resume Live Evidence`
- `Resume Live Evidence`

### Unified evidence capture

Capture export is no longer tied only to Protocol Trace.

The capture writer now uses selected rows from the active evidence workspace:

- Protocol Trace
- Evidence Summary

The capture manifest includes:

- `SourceWorkspace`
- capture kind such as `SelectedProtocolTraceRows` or `SelectedEvidenceSummaryRows`.

### Single truth open capture

Opening an `.ariec` capture rebuilds both:

- Protocol Trace
- Evidence Summary

from the same `frames.jsonl` ledger.

This makes the capture file the single source of evidence truth.

### Evidence Summary live hold

Evidence Summary now has its own stable reading hold. When rows are selected or context menu is open:

- incoming evidence continues to be stored,
- visible Evidence Summary is held,
- resume/clear selection synchronizes the view.

## Preserved

- Protocol Trace default workspace.
- Stable Protocol Trace hold/resume.
- Right-click Protocol Trace evidence export.
- `.ariec` capture container structure.
- Trace TXT export.
