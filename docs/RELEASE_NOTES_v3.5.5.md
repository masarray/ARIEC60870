# ARIEC60870 v3.5.5 — Clickable Panel Header Collapse UX

## Changed

### Panel headers now control expand/collapse

The right command panel and bottom status history panel now use a modern disclosure/accordion interaction model:

- the panel header/card is the click target,
- the chevron icon is only a state indicator,
- hover highlights the header area,
- the chevron becomes brighter on hover.

This replaces small standalone expand/collapse buttons.

### Right command panel

The expanded command dock header is now clickable.
The collapsed command dock handle is also clickable and uses the same hover-highlight behavior.

### Bottom status history panel

The status history header is now clickable.
The old `Hide/Show` button has been removed to reduce visual clutter.

## Preserved

- Command dock expand/collapse behavior.
- Status history expand/collapse behavior.
- Left-rail Auto Scroll Latest toggle.
- Protocol Trace and Evidence Summary evidence workflow.
- Unified `.ariec` evidence capture.
