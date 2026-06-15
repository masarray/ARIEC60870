# ARIEC60870 v1.7.4 — Visual De-Clutter Audit Pass

This release is a focused UI/UX polish pass after field-style review of the WPF Protocol Lab workspace.

## UX changes

- Reduced visible explanatory text across the main workspace.
- Moved long guidance into concise premium tooltips.
- Removed redundant protocol badge text in the left rail.
- Converted Connect / Disconnect into one toggle-style rail button.
- Improved command dock clarity with compact labels and safer open/close button styling.
- Added soft green styling for Open actions and soft/red styling for Close actions.
- Improved TX/RX visual hierarchy: TX is blue, RX is green, warnings/state markers remain status-colored.
- Reduced segmented navigation animation overhead by animating only the slider position.
- Increased scrollbar thumb minimum size and thickness for high-row-count grids.
- Added stronger modern button shadow treatment.

## Notes

- Raw protocol detail remains in Frame Trace and inspector panels.
- Operator-facing tabs are kept cleaner to reduce visual fatigue.
- Full .NET build validation still requires a Windows machine or environment with the .NET SDK installed.
