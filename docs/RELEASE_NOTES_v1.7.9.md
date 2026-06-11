# ARIEC60870 v1.7.9 — Command Dock Left/Right Chevron + Interpreter Tone

## Fixed

- Command dock collapse/expand now uses left/right circle chevron icon semantics.
  - Expanded dock header shows right chevron to collapse the right panel.
  - Collapsed mini button shows left chevron to reopen the dock.
- Added Lucide-compatible `LucideCircleChevronLeft` and `LucideCircleChevronRight` geometries.
- Frame Interpreter panel now follows the selected frame tone:
  - TX: soft blue
  - RX: soft green
  - Error/timeout/NACK: soft red
  - Neutral: default light panel

## UX Rationale

The command dock is a right-side panel, so left/right chevrons communicate the actual spatial movement better than up/down chevrons. The frame interpreter now visually stays linked to the selected TX/RX/error row, improving trace readability without adding tooltip objects to grid rows.
