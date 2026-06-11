# ARIEC60870 v1.2.23 — Sliding Segmented Nav + Custom Rail Icons + Modern ComboBox

## Fixed / improved

- Replaced the TabControl header workflow with a custom fluid segmented navbar.
- Added a real sliding selected pill that moves left/right when the selected tab changes.
- Kept hover grow and press shrink motion on segmented nav items without using invalid TabItem properties.
- Reworked the left command rail as icon + small caption buttons, inspired by clean web-app toolbars.
- Increased rail button hit area and internal spacing so icons/captions do not feel clipped.
- Restyled ComboBox controls with rounded web-style chrome, subtle hover/focus borders, and clean dropdown items so setup fields visually match text fields.

## Product intent

The main window should feel like an instrument cockpit: setup lives behind a modal, the left rail gives quick commands, the header shows live indicators, and the main area remains focused on protocol observation.
