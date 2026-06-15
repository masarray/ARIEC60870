# ARIEC60870 v1.2.4 - Compile Fix + IEC-103 Slave Simulator

## Fixed

- Fixed WPF `MC4111 Cannot find the Trigger target 'RowBrush'` in `ModernTheme.xaml`.
- Replaced the DataGrid row template trigger with compile-safe WPF setters targeting `RowBorder` directly.

## Added

- Added deterministic IEC-103 slave simulator foundation.
- New CLI command:

```bat
dotnet run --project src\ARIEC60870.Cli -- slave --port COM2 --baud 9600 --link 1 --ca 1 --duration 300
```

- Slave simulator responds to:
  - reset remote link,
  - reset FCB,
  - clock sync,
  - General Interrogation,
  - Class 1 polling,
  - Class 2 polling.

## Simulator behavior

- GI request seeds a Class 1 response queue.
- Class 1 drain returns DPI(TM), DPI(RT), Identification, and GI END.
- Empty Class 1 queue returns NO DATA.
- Class 2 polling exposes ACD=1 when Class 1 data is pending.
- Optional spontaneous demo event validates event-drain behavior after background polling.

## Negative test modes

- `--missing-gi-end`
- `--dfc-busy`
- `--silent`
- `--bad-checksum`
- `--no-spontaneous`

## Notes

The slave simulator is a supporting test environment. ARIEC60870 remains an IEC-103 active master tester and analyzer first.
