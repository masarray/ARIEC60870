# ARIEC60870 v3.5.2 — Auto Scroll Latest Toolbar Toggle

## Improved

### One-icon auto-scroll control

The Line Monitor toolbar now uses a single icon toggle for live scrolling:

- tooltip: `Auto scroll latest`
- ON: the active workspace follows the latest stored evidence
- OFF: the active workspace is held for stable reading and selection

This replaces the previous text-heavy controls:

- Follow Live
- Resume
- Latest

The behavior remains the same internally, but the UX is cleaner and more aligned with modern log/protocol viewer patterns.

### Compact visual status

The live status chip now reports:

- `AUTO` when auto-scroll is enabled,
- `HOLD` when the view is protected for reading/selection,
- pending row count while live data is captured but not rendered into the visible view.

## Preserved

- Professional Line Monitor hold/follow engine.
- Evidence Summary multi-select.
- Protocol Trace multi-select.
- Unified `.ariec` evidence capture.
- Context-menu export.
- Protocol Trace default workspace.
