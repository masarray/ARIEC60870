# ARIEC60870 v1.7.8 — Grid Tooltip Removal + Command Dock Chevron

## Fixed

- Removed grid-row/cell tooltip dependency from grid text styles.
- Traffic row coloring is intentionally scoped only to Operator Evidence and Frame Trace.
- Timeout/no-response/NACK rows are classified as error tone.
- Command dock header is now a clickable collapse/expand bar with a Lucide circle chevron icon.
- The old `Hide` text button is removed from the command dock header.

## UX / Performance Policy

- Tooltips are allowed only for buttons, cards, and explanatory panels.
- Data grid rows/cells must not create per-cell tooltips because that increases visual/object churn on large traces.
- Color/highlight belongs only to forensic traffic grids, not every table.
