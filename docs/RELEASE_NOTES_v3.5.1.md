# ARIEC60870 v3.5.1 — UX Layout Cleanup + Public Preview Polish

## Improved

### Cleaner workspace header

The workspace card header is now split into two clearer rows:

1. workspace navigation row,
2. line monitor command bar.

This avoids forcing navigation, live status, follow/resume controls, trace mode, and export actions into one crowded row.

### Navigation row

Workspace navigation now has its own row with horizontal overflow support. The compact live status chip remains visible on the right:

- `LIVE FOLLOW / HOLD`
- pending row counter.

### Line Monitor command bar

Line monitor controls now sit in a dedicated command bar:

- `Follow Live`
- `Resume`
- `Latest`
- `Trace` verbosity mode
- `Export`

The command bar uses compact spacing and wrapping-friendly layout so the UI remains readable on smaller windows.

## Preserved

- Professional Line Monitor UX engine.
- Follow/Hold behavior.
- Resume and Latest actions.
- Protocol Trace default workspace.
- Evidence Summary card view.
- Unified `.ariec` evidence capture.
- Context-menu evidence export.
