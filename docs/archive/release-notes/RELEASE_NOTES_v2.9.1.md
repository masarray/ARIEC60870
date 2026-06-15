# ARIEC60870 v2.9.1 — Startup Null-Safe Session Log Fix

## Fixed

Resolved startup/lifecycle crash:

- `System.NullReferenceException`
- source: `MainWindow.AppendSessionLog(...)`

Root cause: `TraceVerbosityComboBox.SelectionChanged` can fire while XAML is still loading, before `SessionLogBox` and related UI controls are fully ready.

## Changed

- `AppendSessionLog(...)` is now null-safe.
- `TraceVerbosityComboBox_SelectionChanged(...)` only writes to session log after the window is loaded.
- `AddStatusHistoryRow(...)` is defensive when status UI is not ready.
- `ClearSessionView(...)` uses null-safe `SessionLogBox?.Clear()`.

## Preserved

- Trace verbosity governor
- Protected evidence mode
- Priority evidence router
- Adaptive flush budget
- Runtime ring buffer store
- No `RemoveAt(0)` in main UI path
