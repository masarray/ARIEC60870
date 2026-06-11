# ARIEC60870 v1.7.6 — Responsive UX / Traffic Tone Pass

## Fixed

- TX/RX/error coloring now applies to the full visible row, not only the `Dir` cell.
- Timeout and fault rows are classified as `Error` traffic tone and render in red.
- Main command and rail buttons now have stronger hover/press feedback: soft shadow at rest, bigger shadow and slight lift on hover, and compression on click.
- Segmented navigation now avoids expensive width animation during high-volume data sessions. Active selection changes are immediate and less laggy.
- Data grids use stricter virtualization settings and deferred scrolling.
- UI queue flush is now smaller and more frequent to reduce frame spikes under thousands of protocol rows.
- Visible row caps are reduced to keep the desktop responsive while the full session evidence remains available through the engine/reporting pipeline.
- Scrollbar thumb is enlarged again and kept visually prominent.

## Notes

This pass follows WPF performance guidance: keep virtualization enabled, use recycling, avoid disabling `CanContentScroll`, use deferred scrolling when rendering is heavy, and avoid unnecessary animation clocks/effects in high-volume views.
