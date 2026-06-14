# ARIEC60870 v3.5.4 — Left Rail Auto Scroll Toggle Cleanup

## Changed

### Auto Scroll Latest moved to the left rail

`Auto scroll latest` is now a left-rail tool-mode toggle instead of a center workspace toolbar control.

This keeps the Protocol Trace / Evidence Summary workspace visually clean for reading and evidence selection.

### Removed center toolbar noise

The following center toolbar elements were removed:

- `LIVE AUTO`
- `0 pending`
- Line Monitor command bar
- Follow Live text control
- Resume text button
- Latest text button

### Context-aware enable state

The Auto Scroll Latest toggle is enabled only when the active workspace is:

- Protocol Trace
- Evidence Summary

For other tabs, the toggle is disabled.

### Toggle visual state

The icon and caption now reflect state:

- ON: blue down-chevron, `Auto`
- OFF / inactive: muted right-chevron, `Hold`

## Preserved

- Auto-scroll runtime follow behavior.
- Auto-scroll turns off when user selects/right-clicks evidence rows.
- Protocol Trace and Evidence Summary multi-select.
- Unified `.ariec` evidence capture.
- Protocol Trace default workspace.
