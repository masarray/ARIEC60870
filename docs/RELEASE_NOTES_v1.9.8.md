# ARIEC60870 v1.9.8 — Auto CA Learning Compile Fix

## Fixed

Resolved missing method declarations in `Iec101MasterSession.cs`:

- `TryRetryGiUsingObservedCommonAddressAsync`
- `SettingsForCommonAddress`
- `ObserveAsduCommonAddress`

These methods were referenced by the v1.9.7 Auto ASDU CA Learning patch but were not inserted into the class body.

## Validation

- MainWindow.xaml XML parse: OK
- ModernTheme.xaml XML parse: OK
- Required CA-learning method declarations: OK
- Main C# brace balance: OK
- IEC101 C# brace balance: OK
- ZIP integrity: OK
