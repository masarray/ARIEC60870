# ARIEC60870 v1.9.6 — Group GI Fallback Compile Fix

## Fixed

- Added the missing `RunGroupInterrogationFallbackAsync(...)` method in `Iec101MasterSession`.
- Resolves compiler errors:
  - `CS0103: The name 'RunGroupInterrogationFallbackAsync' does not exist in the current context`

## Behaviour

When station GI `QOI=20` is negatively confirmed, the IEC-101 session now tries bounded group interrogation `QOI=21..36`, drains accepted group responses, then continues Class 2/background polling.

## Validation

- MainWindow.xaml XML parse: OK
- ModernTheme.xaml XML parse: OK
- `RunGroupInterrogationFallbackAsync` calls: present
- `RunGroupInterrogationFallbackAsync` method: present
- Main C# brace balance: OK
- IEC101 C# brace balance: OK
- ZIP integrity: OK
