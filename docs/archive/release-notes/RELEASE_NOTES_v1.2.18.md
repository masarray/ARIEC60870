# ARIEC60870 v1.2.18 — Read-only Grids + Icon Rail + Line Monitor Fit

## Fixed
- Prevented WPF DataGrid edit mode from being entered on click.
- Fixed runtime binding exception such as:
  `A TwoWay or OneWayToSource binding cannot work on the read-only property 'Signal' of type 'ValueRow'.`
- All monitoring grids are now read-only evidence surfaces.

## Improved UI
- Replaced text-only left command rail buttons with compact vector icon buttons and hover tooltips.
- Added safer rail spacing so hover/shadow effects do not clip against the card edge.
- Enlarged the line-monitor grid area by reducing the right interpreter width.
- Removed the `#` sequence column from the line-monitor grid.
- Enabled wrapped cells for the line monitor so commissioning meaning and frame content are visible without routine horizontal scrolling.
- Simplified the frame interpreter visual style: internal rounded/bordered boxes were reduced so the inspector reads cleaner and less boxed-in.

## Product rule
- Main protocol grids are evidence views, not editors.
- Frame trace remains the primary monitor surface; the right interpreter explains the selected frame.
