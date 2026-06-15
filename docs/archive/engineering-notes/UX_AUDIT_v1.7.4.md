# UX Audit v1.7.4

## Findings addressed

1. Too much text was visible at once. The main workspace now uses concise labels and moves explanations into tooltips.
2. The left rail protocol text badge was redundant with the protocol logo and setup summary, so it was removed.
3. Connect and Disconnect were separate buttons, creating unnecessary vertical rail weight. They are now a single toggle-style command.
4. Command lifecycle buttons need strong semantic color. Open uses green, Close uses red, while select actions use softer styling.
5. Segmented navigation animation previously animated both width and position, which could feel laggy. Width is now snapped and only the slider position is animated.
6. Scrollbar thumbs were still too short for large grids. The minimum thumb size is increased and the track/thumb styling is made more visible.
7. Communication direction needs color hierarchy. TX is blue, RX is green, status/error states use warning/error colors.

## Remaining watch items

- Verify WPF runtime behaviour after build on Windows, especially the connect/disconnect toggle state during transport failure.
- Confirm custom scrollbar template behaviour inside DataGrid on all target Windows display scale settings.
- Continue reducing non-essential explanatory text in secondary windows if the same visual density issue appears there.
