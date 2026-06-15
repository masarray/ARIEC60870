# ARIEC60870 v3.4.5 — Lightweight Protocol Trace Multi-Select + Context Menu Evidence Export

## Fixed / Improved

### Lightweight multi-select engine

Protocol Trace selection now uses row-level `MouseEnter` events during drag instead of continuously relying on heavy hit testing.

Supported gestures:

- click row to select,
- Shift-click to select a range,
- Ctrl-click to toggle a row,
- click-drag top-to-bottom,
- click-drag bottom-to-top.

### Deferred inspector rendering during selection

While user is dragging/selecting rows, the frame interpreter rendering is deferred. The inspector updates after selection completes, reducing UI churn.

### Context menu evidence workflow

Right-click on Protocol Trace now offers common desktop-style actions:

- `Export Selected Capture File...`
- `Export Selected Trace Text...`
- `Select All Visible Trace Rows`
- `Clear Selection`

Right-clicking a row that is not already selected selects it first, matching common desktop app behaviour.

## Preserved

- Left rail Save Cap
- Left rail Trace TXT
- `.ariec` selected capture
- `.txt` selected/visible Protocol Trace export
- Open capture/offline review
- Protocol Trace as default workspace
