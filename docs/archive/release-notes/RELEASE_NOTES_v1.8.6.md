# ARIEC60870 v1.8.6 — Protocol Trace Binding Syntax Fix

## Fixed

- Fixed XAML compiler error `MC3091` caused by malformed `<Run Text>` binding syntax.
- Protocol Trace line monitor bindings now use explicit valid WPF syntax:
  `Text="{Binding Path=Direction, Mode=OneWay}"`
- Defensive cleanup removes any accidental empty/malformed Binding expressions.

## Validation

- MainWindow.xaml XML parse: OK
- ModernTheme.xaml XML parse: OK
- Run.Text binding syntax audit: OK
- C# brace balance: OK
- ZIP integrity: OK
