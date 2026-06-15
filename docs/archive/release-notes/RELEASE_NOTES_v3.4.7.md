# ARIEC60870 v3.4.7 — Evidence Summary Full Reading View

## Improved

### Evidence Summary full-height reading view

Evidence Summary no longer shows the lower inspector panel. The workspace is now dedicated to the readable evidence card/list.

The old inspector controls are kept collapsed only to preserve existing code-behind references, but they no longer consume visual space.

### Richer evidence cards

Evidence Summary cards now include concise protocol metadata directly inside each card:

- signal,
- value,
- quality,
- Type ID,
- COT,
- CA,
- IOA,
- relay/RTU time.

This makes Evidence Summary readable without opening another inspector area.

## Preserved

- Hidden EvidenceGrid remains for export compatibility.
- Stable Protocol Trace view hold/resume.
- Context-menu capture export.
- Open/save `.ariec` capture.
- Protocol Trace default workspace.
