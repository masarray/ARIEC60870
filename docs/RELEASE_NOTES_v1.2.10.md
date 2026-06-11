# ARIEC60870 v1.2.10 — Premium Typography + Collapsible Protocol Inspector

## Purpose

This release tightens the WPF visual system after field UI review. The goal is to keep the app premium, compact, and readable for commissioning engineers while still preserving raw protocol transparency for expert inspection.

## Changes

- Switched WPF text rendering from `Display` to `Ideal` to reduce jagged/rough-looking small text.
- Kept the app font stack centered on `Aptos` with modern Windows fallbacks.
- Reduced visual weight across headings and table headers so the app feels less bold/heavy.
- Made the main product title and sidebar product title smaller and calmer.
- Reduced header/session card height and KPI card padding.
- Compact metric cards now consume less vertical space and behave more like useful status indicators, not decorative panels.
- Added an expandable/collapsible Frame Trace inspector.
- Frame Trace inspector is expanded by default but can be collapsed to give the table more room.
- Selected-frame explanation now uses a bounded scrollable area so long protocol explanation text remains readable instead of being clipped.
- Raw frame evidence remains available in the same inspector but is visually secondary to the readable explanation.
- Shortened repetitive TX meaning text for Class 1/Class 2 request frames.

## Design rule locked

Raw protocol transparency is still required, but the default user experience must read as:

1. engineering meaning,
2. commissioning action,
3. raw frame evidence on demand.

The UI must not force users to read raw hex or long repeated polling sentences as the main workflow.
