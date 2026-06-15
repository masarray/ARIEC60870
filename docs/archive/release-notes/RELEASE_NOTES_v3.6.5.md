# ARIEC60870 v3.6.5 — Report Preview + Clear Export Report Action

## Fixed

### Report export is now visible

The left rail action is now clearly labeled `Report` instead of a generic `Export`, and it is available even before a completed run. If no evidence exists, ARIEC shows a useful message.

### Report Preview workspace

A new `Report Preview` tab has been added.

It includes:

- `Refresh`
- `Export HTML / PDF`
- embedded HTML preview browser

The preview uses the same standalone report generator as export.

## Report workflow

1. Run a session or open a `.ariec` capture.
2. Open `Report Preview`.
3. Click `Refresh`.
4. Click `Export HTML / PDF`.
5. Open the generated HTML in a browser and print/save as PDF.

## Preserved

- utility-style standalone HTML report.
- Smart Capture Rules.
- Protocol Trace default workspace.
- Unified `.ariec` capture.
- Left-rail Auto Scroll Latest.
