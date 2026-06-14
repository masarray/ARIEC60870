# ARIEC60870 v3.4.4 — Protocol Trace Drag Direction Fix

## Fixed

### Drag selection top-to-bottom

Protocol Trace block selection no longer uses mouse capture for row selection. The previous implementation could behave asymmetrically, especially when dragging from top to bottom.

### Shift-click range

Shift-click uses the explicit selection anchor and the current hovered row resolved from the actual ListBox item container.

### Multi-select stability

The selection engine no longer writes `SelectedItem` during range selection. It updates `SelectedItems` directly and focuses the current container only for keyboard continuity.

## Technical change

Row selection now resolves the row index from:

1. `ItemsControl.ContainerFromElement(...)` using the actual input source,
2. `VisualTreeHelper.HitTest(...)` fallback,
3. visible realized containers via `ItemContainerGenerator.ContainerFromIndex(...)`.

## Preserved

- Shift range selection
- Ctrl toggle selection
- Click-drag block selection
- Trace TXT export
- Save selected `.ariec` capture
- Open `.ariec` capture
- Protocol Trace as default workspace
