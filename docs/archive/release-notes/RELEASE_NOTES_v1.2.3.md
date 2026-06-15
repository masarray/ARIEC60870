# Release Notes — v1.2.3 Responsive AR Layout + Stable Session Header

## Fixes

- Fixed the top `SESSION STATE` area visually jittering during polling. The header now shows stable session phase only, while fast protocol state remains in Operator Evidence / Frame Trace.
- Prevented long status messages from resizing the header by using fixed-height, trimmed status fields.
- Reduced workspace margins and sidebar width to better match the mature engineering layout identity.
- Added explicit horizontal and vertical scrolling on large DataGrid views.
- Tightened TabControl, cards, typography, and DataGrid row hover behavior toward the mature engineering software style.

## Product direction preserved

- Operator-first output remains the default.
- Raw hex remains visible in Frame Trace and selected evidence inspector.
- Event Log remains relay-timestamp and edge/state-change oriented.
- Mapping profile remains user-owned; no built-in vendor mapping is introduced.
- High-volume polling still uses bounded visible buffers and batched UI rendering.
