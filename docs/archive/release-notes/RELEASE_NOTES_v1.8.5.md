# ARIEC60870 v1.8.5 — Protocol Trace Run Binding Fix

## Fixed

- Fixed `XamlParseException` in Protocol Trace line monitor.
- All `Run.Text` bindings inside the Protocol Trace multiline template now use `Mode=OneWay`.
- This prevents WPF from attempting a TwoWay/OneWayToSource binding against read-only `EvidenceRow` properties such as `Direction`.

## Affected area

- `MainWindow.xaml`
- Protocol Trace ListBox item template

## Validation

- MainWindow.xaml XML parse: OK
- ModernTheme.xaml XML parse: OK
- Run.Text binding audit: all Run bindings in Protocol Trace include `Mode=OneWay`
- C# brace balance: OK
- ZIP integrity: OK
