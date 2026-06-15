# ARIEC60870 v1.2.19 — Grid Export, Stable Values, and Interpreter Polish

## Fixes

- Removed the `Copy raw` and `Copy decode` buttons from the frame interpreter to keep the right inspector focused on interpretation.
- Restored protocol-aware hover linking: hovering a raw hex field group highlights the related decoded content block, and hovering a decoded block highlights the related raw field group.
- Reduced global heavy typography. Aptos remains the only UI font; most text is normal weight, with medium weight reserved for key signal address, value, and relay time information.

## Value Viewer

- Value Viewer row order is now stable during polling.
- Existing signal rows are updated in place instead of being moved to the top.
- New signals are inserted in deterministic order: digital/status points first, measurands below.

## Event Log

- SOE timestamp now includes date and time. When IEC-103 only provides time-of-day, the app combines relay time with the local arrival date for audit readability.

## Grid usability

- All monitoring grids now support multi-select.
- Right-click copy is available through the DataGrid context menu.
- Copied grid content is tab-separated text suitable for paste into Excel, Notepad, issue reports, or FAT/SAT evidence notes.

## Data export

- Added an `Export Data` button aligned with the segmented tab area.
- Exports the currently selected grid tab as tab-separated `.txt`.
- `Session Notes` is intentionally excluded because it is already a text log, not a tabular evidence grid.
