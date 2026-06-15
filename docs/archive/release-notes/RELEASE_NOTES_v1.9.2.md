# ARIEC60870 v1.9.2 — Compile Fix for SCADA GI Cleanup

## Fixed

- Removed stale references left from the removed v1.9.0 mass read-recovery workflow:
  - `_giReadRecoveryQueuedKeys`
  - `_giReadRecoveryQueued`
  - `_giReadRecoveryReportAfterUtc`
- `ClearSessionView` now resets the v1.9.1 SCADA-style GI collection window fields:
  - `_giClass2CollectionWindowActive`
  - `_giClass2CollectionUntilUtc`

## Validation

- MainWindow.xaml XML parse: OK
- ModernTheme.xaml XML parse: OK
- No stale GI read-recovery references remain in MainWindow.xaml.cs
- Main C# brace balance: OK
- IEC101 C# brace balance: OK
- ZIP integrity: OK
