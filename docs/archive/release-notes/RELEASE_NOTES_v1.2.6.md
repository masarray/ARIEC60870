# ARIEC60870 v1.2.6 — XAML Resource Setter Compile Fix

## Fixed

- Fixed WPF startup crash: `XamlParseException: Set property System.Windows.Setter.Property threw an exception` in `ModernTheme.xaml`.
- Removed invalid DataGrid style setter for `Resources`. `FrameworkElement.Resources` is not safe to set through a `Style` setter in this WPF template path and caused `ArgumentNullException: property`.
- Kept the v1.2.5 visual-fit work, stable session header, wider trace columns, DataGrid virtualization, diagnostics pipeline, and serial close diagnostics.

## Product scope retained

- Active IEC-103 master tester remains the primary product direction.
- Slave simulator from v1.2.4 remains included for deterministic master testing.
- Raw frame trace remains an expert/evidence view; operator evidence remains the primary user-facing view.
