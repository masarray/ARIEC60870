# ARIEC60870 v3.5.3 — Auto Scroll Latest Runtime Follow Fix

## Fixed

### Auto Scroll Latest now really follows the newest row

When the `Auto scroll latest` toggle is ON, the active line monitor workspace now scrolls to the latest row after new evidence rows are flushed into the visible view.

This applies to:

- Protocol Trace
- Evidence Summary

### Selection switches to HOLD

When the user starts selecting or right-clicking rows, the app now automatically turns Auto Scroll Latest OFF.

This removes the ambiguous state where the icon looked active but the view was held because rows were selected.

## Behavior

- Auto Scroll ON: always follow latest visible row on incoming data.
- User selection / right click: Auto Scroll turns OFF and the view enters HOLD.
- Toggle Auto Scroll ON again: selection clears, latest data syncs, view jumps to latest.

## Preserved

- One-icon toolbar UX.
- Stable reading hold.
- Evidence Summary multi-select.
- Protocol Trace multi-select.
- Unified `.ariec` evidence capture.
- Protocol Trace default workspace.
