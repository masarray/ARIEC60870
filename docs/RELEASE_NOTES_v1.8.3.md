# ARIEC60870 v1.8.3 — Lazy Evidence/Trace Snapshot + Signal List Popup + IEC-101 GI Value Proof

## UI / UX

- Removed the heavy Signal List workspace tab from the main TabControl.
- The left `Signals` rail button now opens the existing Signal List Editor as a popup window.
- Segmented navigation now contains only live workspaces:
  - Evidence Summary
  - Protocol Trace
  - Value Viewer
  - Event Log
  - AutoTest
  - Findings
  - Diagnostics
  - Session Notes
- Evidence Summary and Protocol Trace now use active-tab snapshot stores:
  - Incoming rows are retained in bounded stores.
  - The visible ObservableCollection is only populated when its tab is active.
  - When inactive, the visible collection is cleared to avoid unnecessary grid rendering pressure.

## IEC-101 GI / Value Viewer

- Value Viewer is now seeded with expected monitor points from the active IOA profile at session start.
- Missing GI/background values remain visible as `waiting for GI / scan` instead of disappearing.
- Received values replace the expected placeholder row using the same IOA key.
- Added GI completeness watch:
  - Tracks expected profile IOAs.
  - Tracks received IOAs.
  - On ACTTERM/activation termination, warns if profile points are still missing.
- Added post-GI Class 2 verification sweep for IEC-101:
  - After the GI Class 1 drain, the master performs a short bounded Class 2 sweep.
  - This helps RTUs that expose some background values outside the Class 1 GI queue populate the Value Viewer.

## Notes

This is the first performance architecture pass, not the final Protocol Trace line-monitor refactor. The next step is to replace Protocol Trace DataGrid with a lightweight virtualized mono line monitor.
